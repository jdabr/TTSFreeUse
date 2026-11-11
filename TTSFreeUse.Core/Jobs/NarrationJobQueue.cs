using System.Collections.Concurrent;
using System.Threading.Channels;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Core.Jobs
{
    /// <summary>
    /// Runs narrations one at a time in the background and saves the results
    /// to the <see cref="AudioLibrary"/>. One at a time is deliberate: the voice
    /// engine already uses all the CPU it can.
    /// </summary>
    public class NarrationJobQueue
    {
        /// <summary>Most conversions that can be waiting or running at once.</summary>
        public const int MaxActiveJobs = 20;

        /// <summary>Finished jobs kept in the list; older ones are dropped (their files stay in the library).</summary>
        public const int MaxFinishedJobs = 50;

        /// <summary>Longest title kept (it also names the audio file and its tags).</summary>
        public const int MaxTitleLength = 200;

        private readonly NarrationPipeline _pipeline;
        private readonly AudioLibrary _library;
        private readonly Ffmpeg _ffmpeg;
        private readonly Channel<NarrationJob> _queue = Channel.CreateUnbounded<NarrationJob>();
        private readonly ConcurrentDictionary<Guid, NarrationJob> _jobs = new();

        // Makes "start" (by the worker) and "cancel before it starts" (by the
        // user) one-or-the-other, so a cancelled job can never begin anyway.
        private readonly object _startLock = new();

        public NarrationJobQueue(NarrationPipeline pipeline, AudioLibrary library, Ffmpeg ffmpeg)
        {
            _pipeline = pipeline;
            _library = library;
            _ffmpeg = ffmpeg;
        }

        /// <summary>
        /// Prepares <paramref name="rawText"/> and queues it. Throws
        /// <see cref="ArgumentException"/> (user-readable) for empty text or a
        /// format that can't be produced on this machine.
        /// </summary>
        public NarrationJob Enqueue(string title, string rawText, TtsOptions options, AudioFormat format)
        {
            if (!Enum.IsDefined(format))
                throw new ArgumentException($"Unknown audio format. Use one of: {string.Join(", ", Enum.GetNames<AudioFormat>())}.");
            if (format.NeedsEncoder() && !_ffmpeg.IsObtainable)
                throw new ArgumentException(Ffmpeg.InstallInstructions);
            if (_jobs.Values.Count(j => j.IsActive) >= MaxActiveJobs)
                throw new ArgumentException(
                    $"There are already {MaxActiveJobs} conversions waiting. Please wait for some to finish.");

            var chunks = _pipeline.Prepare(rawText);
            if (chunks.Count == 0)
                throw new ArgumentException(
                    "No readable text found. Scanned PDFs (images of pages) aren't supported yet.");

            var job = new NarrationJob(TidyTitle(title), chunks, options, format);
            _jobs[job.Id] = job;
            _queue.Writer.TryWrite(job);

            // Fetch the converter while narration runs, so it's ready at the end.
            if (format.NeedsEncoder())
                _ = _ffmpeg.EnsureAvailableAsync().ContinueWith(_ => { }, TaskScheduler.Default);

            return job;
        }

        public NarrationJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

        /// <summary>Jobs from this session, newest first.</summary>
        public IReadOnlyList<NarrationJob> List() =>
            _jobs.Values.OrderByDescending(j => j.Created).ToList();

        public bool Cancel(Guid id)
        {
            if (!_jobs.TryGetValue(id, out var job))
                return false;
            lock (_startLock)
            {
                if (!job.IsActive)
                    return false;
                // Not started yet: the worker will skip it.
                if (job.State == JobState.Queued)
                    Finish(job, JobState.Cancelled);
            }
            // Started: narration or conversion stops at the next opportunity.
            job.Cancellation.Cancel();
            return true;
        }

        /// <summary>Removes a finished job from the list (its audio file is kept).</summary>
        public bool Dismiss(Guid id) =>
            _jobs.TryGetValue(id, out var job) && !job.IsActive && _jobs.TryRemove(id, out _);

        /// <summary>Processes jobs until <paramref name="stoppingToken"/> fires.</summary>
        public async Task RunAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
                await ProcessAsync(job, stoppingToken);
        }

        /// <summary>Runs one job, unless it was cancelled before it could start.</summary>
        internal async Task ProcessAsync(NarrationJob job, CancellationToken stoppingToken)
        {
            lock (_startLock)
            {
                if (job.State != JobState.Queued)
                    return;
                job.State = JobState.Narrating;
            }
            job.Started = DateTimeOffset.UtcNow;
            job.StatusText = "Reading aloud…";

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            var ct = linked.Token;

            // The audio is put together in private temp files and only moved
            // into the library once it's complete.
            string? tempWav = null, tempEncoded = null;
            try
            {
                await using (var wavStream = TempAudioFile.Create($"{job.Id:N}.wav"))
                {
                    tempWav = wavStream.Name;
                    var progress = new InlineProgress(p => job.ChunksDone = p.ChunksDone);
                    job.AudioDuration = await _pipeline.NarrateAsync(job.Chunks, wavStream, job.Options, progress, ct);
                }

                var finished = tempWav;
                if (job.Format.NeedsEncoder())
                {
                    job.State = JobState.Encoding;
                    var converting = "Converting to " + job.Format.ToString().ToUpperInvariant() + "…";
                    job.StatusText = converting;
                    var waitForConverter = _ffmpeg.EnsureAvailableAsync(ct);
                    // Show download progress if the converter is still being fetched.
                    while (!waitForConverter.IsCompleted)
                    {
                        job.StatusText = _ffmpeg.Status ?? converting;
                        await Task.WhenAny(waitForConverter, Task.Delay(500, ct));
                        ct.ThrowIfCancellationRequested();
                    }
                    await waitForConverter;
                    job.StatusText = converting;

                    await using (var encoded = TempAudioFile.Create($"{job.Id:N}{job.Format.Extension()}"))
                        tempEncoded = encoded.Name;
                    var comment = $"Narrated by TTSFreeUse (voice: {job.Options.Voice ?? _pipeline.Engine.DefaultVoice})";
                    await _ffmpeg.EncodeAsync(tempWav, tempEncoded, job.Format,
                        new AudioMetadata(job.Title, comment), ct);
                    finished = tempEncoded;
                }

                ct.ThrowIfCancellationRequested();
                job.FileName = Path.GetFileName(_library.Add(finished, job.Title, job.Format));
                Finish(job, JobState.Completed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Finish(job, JobState.Cancelled);
                if (stoppingToken.IsCancellationRequested)
                    throw;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                Finish(job, JobState.Failed);
            }
            finally
            {
                DeleteQuietly(tempWav);
                DeleteQuietly(tempEncoded);
            }
        }

        /// <summary>One line of at most <see cref="MaxTitleLength"/> characters, without control characters.</summary>
        private static string TidyTitle(string title)
        {
            var words = new string(title.Select(c => char.IsControl(c) ? ' ' : c).ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tidy = string.Join(' ', words);
            if (tidy.Length > MaxTitleLength)
                tidy = tidy[..MaxTitleLength].TrimEnd();
            return tidy.Length > 0 ? tidy : "Narration";
        }

        private void Finish(NarrationJob job, JobState state)
        {
            job.State = state;
            job.StatusText = null;
            job.ReleaseText();
            ForgetOldFinishedJobs();
        }

        private void ForgetOldFinishedJobs()
        {
            var finished = _jobs.Values.Where(j => !j.IsActive).OrderByDescending(j => j.Created).ToList();
            foreach (var old in finished.Skip(MaxFinishedJobs))
                _jobs.TryRemove(old.Id, out _);
        }

        private static void DeleteQuietly(string? path)
        {
            if (path == null)
                return;
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>Reports synchronously on the worker (Progress&lt;T&gt; would post to the thread pool).</summary>
        private class InlineProgress(Action<NarrationProgress> report) : IProgress<NarrationProgress>
        {
            public void Report(NarrationProgress value) => report(value);
        }
    }
}
