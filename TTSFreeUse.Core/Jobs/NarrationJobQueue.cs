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
        private readonly NarrationPipeline _pipeline;
        private readonly AudioLibrary _library;
        private readonly Ffmpeg _ffmpeg;
        private readonly Channel<NarrationJob> _queue = Channel.CreateUnbounded<NarrationJob>();
        private readonly ConcurrentDictionary<Guid, NarrationJob> _jobs = new();

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
            if (format.NeedsEncoder() && !_ffmpeg.IsObtainable)
                throw new ArgumentException(Ffmpeg.InstallInstructions);

            var chunks = _pipeline.Prepare(rawText);
            if (chunks.Count == 0)
                throw new ArgumentException(
                    "No readable text found. Scanned PDFs (images of pages) aren't supported yet.");

            var job = new NarrationJob(title, chunks, options, format);
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
            if (!_jobs.TryGetValue(id, out var job) || !job.IsActive)
                return false;
            job.Cancellation.Cancel();
            if (job.State == JobState.Queued)
                Finish(job, JobState.Cancelled);
            return true;
        }

        /// <summary>Removes a finished job from the list (its audio file is kept).</summary>
        public bool Dismiss(Guid id) =>
            _jobs.TryGetValue(id, out var job) && !job.IsActive && _jobs.TryRemove(id, out _);

        /// <summary>Processes jobs until <paramref name="stoppingToken"/> fires.</summary>
        public async Task RunAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (job.State != JobState.Queued)
                    continue; // cancelled while waiting
                await ProcessAsync(job, stoppingToken);
            }
        }

        internal async Task ProcessAsync(NarrationJob job, CancellationToken stoppingToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            var ct = linked.Token;

            var tempWav = Path.Combine(Path.GetTempPath(), $"ttsfreeuse-{job.Id:N}.wav");
            string? outputPath = null;
            try
            {
                job.State = JobState.Narrating;
                job.Started = DateTimeOffset.UtcNow;
                job.StatusText = "Reading aloud…";

                await using (var wavStream = File.Create(tempWav))
                {
                    var progress = new InlineProgress(p => job.ChunksDone = p.ChunksDone);
                    job.AudioDuration = await _pipeline.NarrateAsync(job.Chunks, wavStream, job.Options, progress, ct);
                }

                outputPath = _library.ReservePath(job.Title, job.Format);
                if (job.Format.NeedsEncoder())
                {
                    job.State = JobState.Encoding;
                    job.StatusText = "Converting to " + job.Format.ToString().ToUpperInvariant() + "…";
                    var waitForConverter = _ffmpeg.EnsureAvailableAsync(ct);
                    if (!waitForConverter.IsCompleted)
                    {
                        // Surface download progress while waiting.
                        while (!waitForConverter.IsCompleted)
                        {
                            job.StatusText = _ffmpeg.Status ?? job.StatusText;
                            await Task.WhenAny(waitForConverter, Task.Delay(500, ct));
                        }
                        job.StatusText = "Converting to " + job.Format.ToString().ToUpperInvariant() + "…";
                    }
                    await waitForConverter;

                    var comment = $"Narrated by TTSFreeUse (voice: {job.Options.Voice ?? _pipeline.Engine.DefaultVoice})";
                    await _ffmpeg.EncodeAsync(tempWav, outputPath, job.Format,
                        new AudioMetadata(job.Title, comment), ct);
                }
                else
                {
                    File.Copy(tempWav, outputPath, overwrite: true);
                }

                job.FileName = Path.GetFileName(outputPath);
                Finish(job, JobState.Completed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                DeleteQuietly(outputPath);
                Finish(job, JobState.Cancelled);
                if (stoppingToken.IsCancellationRequested)
                    throw;
            }
            catch (Exception ex)
            {
                DeleteQuietly(outputPath);
                job.Error = ex.Message;
                Finish(job, JobState.Failed);
            }
            finally
            {
                DeleteQuietly(tempWav);
            }
        }

        private static void Finish(NarrationJob job, JobState state)
        {
            job.State = state;
            job.StatusText = null;
            job.Finished = DateTimeOffset.UtcNow;
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
