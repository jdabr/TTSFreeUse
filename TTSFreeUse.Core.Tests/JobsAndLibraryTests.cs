using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Jobs;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.Transformers;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Core.Tests;

/// <summary>Creates an empty temp folder per test and deletes it afterwards.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ttsfreeuse-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

public class AudioLibraryTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void AddsFinishedFilesUnderUniqueNames()
    {
        var library = new AudioLibrary(Path.Combine(_dir.Path, "library"));
        string Finished(string content)
        {
            var path = Path.Combine(_dir.Path, Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, content);
            return path;
        }

        var first = library.Add(Finished("one"), "Electrical Code: Ch 3?", AudioFormat.Mp3);
        var second = library.Add(Finished("two"), "Electrical Code: Ch 3?", AudioFormat.Mp3);

        Assert.Equal("Electrical Code Ch 3.mp3", Path.GetFileName(first));
        Assert.Equal("Electrical Code Ch 3 (2).mp3", Path.GetFileName(second));
        Assert.Equal("one", File.ReadAllText(first));
        Assert.Equal("two", File.ReadAllText(second));
        Assert.Empty(Directory.GetFiles(_dir.Path)); // moved into the library subfolder, not copied
    }

    [Theory]
    [InlineData("", "Narration")]
    [InlineData("  ...  ", "Narration")]
    [InlineData("a/b\\c", "a b c")]
    // Names older versions of Windows reserve for devices, even with an extension.
    [InlineData("CON", "_CON")]
    [InlineData("nul.backup", "_nul.backup")]
    [InlineData("COM1", "_COM1")]
    [InlineData("Console", "Console")]
    public void SafeNameHandlesOddTitles(string title, string expected)
    {
        Assert.Equal(expected, AudioLibrary.SafeName(title));
    }

    [Fact]
    public void ListsOnlyAudioFilesNewestFirst()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_dir.Path, "old.mp3"), "x");
        File.SetCreationTimeUtc(Path.Combine(_dir.Path, "old.mp3"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(_dir.Path, "new.m4b"), "x");

        var items = new AudioLibrary(_dir.Path).List();

        Assert.Equal(new[] { "new.m4b", "old.mp3" }, items.Select(i => i.FileName));
        Assert.Equal(AudioFormat.M4b, items[0].Format);
    }

    [Theory]
    [InlineData("..\\secret.mp3")]
    [InlineData("../secret.mp3")]
    [InlineData("sub/file.mp3")]
    [InlineData("notes.txt")]
    [InlineData("missing.mp3")]
    [InlineData("")]
    public void RefusesAnythingButExistingAudioFilesInTheFolder(string name)
    {
        File.WriteAllText(Path.Combine(_dir.Path, "notes.txt"), "x");
        var library = new AudioLibrary(Path.Combine(_dir.Path));

        Assert.Null(library.Resolve(name));
        Assert.False(library.Delete(name));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "notes.txt")));
    }

    [Fact]
    public void DeletesAudioFile()
    {
        var path = Path.Combine(_dir.Path, "chapter.mp3");
        File.WriteAllText(path, "x");

        Assert.True(new AudioLibrary(_dir.Path).Delete("chapter.mp3"));
        Assert.False(File.Exists(path));
    }
}

public class FfmpegTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static string ExeName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    [Fact]
    public void Mp3ArgumentsUseLameAtSpeechBitrate()
    {
        var args = Ffmpeg.BuildArguments("in.wav", "out.mp3", AudioFormat.Mp3, new AudioMetadata("My Title", "note"));

        Assert.Equal("in.wav", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Contains("libmp3lame", args);
        Assert.Equal("64k", args[args.ToList().IndexOf("-b:a") + 1]);
        Assert.Contains("title=My Title", args);
        Assert.Contains("comment=note", args);
        Assert.Equal("out.mp3", args[^1]);
    }

    [Fact]
    public void M4bArgumentsUseAacInAudiobookContainer()
    {
        var args = Ffmpeg.BuildArguments("in.wav", "out.m4b", AudioFormat.M4b, new AudioMetadata("T"));

        Assert.Contains("aac", args);
        Assert.Equal("ipod", args[args.ToList().IndexOf("-f") + 1]);
        Assert.DoesNotContain(args, a => a.StartsWith("comment="));
    }

    [Fact]
    public void WavNeedsNoConversion()
    {
        Assert.Throws<ArgumentException>(() =>
            Ffmpeg.BuildArguments("in.wav", "out.wav", AudioFormat.Wav, new AudioMetadata("T")));
    }

    [Fact]
    public void UsesConfiguredPathWhenItExists()
    {
        var exe = Path.Combine(_dir.Path, ExeName);
        File.WriteAllText(exe, "");

        Assert.Equal(exe, new Ffmpeg(new FfmpegOptions { Path = exe }).Locate());
        Assert.Null(new Ffmpeg(new FfmpegOptions { Path = exe + ".missing" }).Locate());
    }

    [Fact]
    public void IgnoresRelativeFoldersInPath()
    {
        // "." or "tools" in PATH means "relative to whatever folder is current",
        // which could hold a planted ffmpeg.exe.
        var exe = Path.Combine(_dir.Path, ExeName);
        File.WriteAllText(exe, "");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, _dir.Path);
        Assert.SkipWhen(Path.IsPathFullyQualified(relative), "The temp folder is on a different drive.");

        Assert.Null(Ffmpeg.FindOnPath(relative, ExeName));
        Assert.Equal(exe, Ffmpeg.FindOnPath(relative + Path.PathSeparator + _dir.Path, ExeName));
    }

    [Fact]
    public async Task WithoutFfmpegAndNoDownloadExplainsHowToInstall()
    {
        var ffmpeg = new Ffmpeg(new FfmpegOptions { Path = Path.Combine(_dir.Path, "none"), AllowDownload = false });

        Assert.False(ffmpeg.IsObtainable);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ffmpeg.EnsureAvailableAsync(TestContext.Current.CancellationToken));
        Assert.Contains("brew install ffmpeg", ex.Message);
    }

    private static byte[] FakeFfmpegZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(content);
            }
            Add($"ffmpeg-1.0-essentials_build/bin/{ExeName}", "fake ffmpeg");
            Add("ffmpeg-1.0-essentials_build/bin/ffprobe.exe", "not needed");
            Add("ffmpeg-1.0-essentials_build/LICENSE", "GPL");
            Add("ffmpeg-1.0-essentials_build/doc/LICENSE", "nested, ignored");
        }
        return ms.ToArray();
    }

    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task DownloadsVerifiesAndInstallsOnlyTheNeededFiles()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic download is Windows-only.");
        var zip = FakeFfmpegZip();
        var package = new FfmpegPackage("1.0", new Uri("https://example.test/ffmpeg.zip"),
            Convert.ToHexString(SHA256.HashData(zip)));
        var handler = new FixedResponseHandler(zip);
        var ffmpeg = new Ffmpeg(new FfmpegOptions { ToolsDirectory = _dir.Path, Path = "" }, package, new HttpClient(handler));

        // Two concurrent callers share one download.
        var ct = TestContext.Current.CancellationToken;
        var results = await Task.WhenAll(ffmpeg.EnsureAvailableAsync(ct), ffmpeg.EnsureAvailableAsync(ct));

        var install = Path.Combine(_dir.Path, "ffmpeg-1.0");
        Assert.All(results, r => Assert.Equal(Path.Combine(install, ExeName), r));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(new[] { "LICENSE", ExeName }.Order(), Directory.GetFiles(install).Select(Path.GetFileName).Order());
        Assert.Equal("GPL", File.ReadAllText(Path.Combine(install, "LICENSE")));
        Assert.Equal(new[] { "ffmpeg-1.0" }, Directory.GetFileSystemEntries(_dir.Path).Select(Path.GetFileName)); // no leftovers
    }

    [Fact]
    public async Task RejectsDownloadThatFailsTheChecksum()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic download is Windows-only.");
        var package = new FfmpegPackage("1.0", new Uri("https://example.test/ffmpeg.zip"), new string('0', 64));
        var ffmpeg = new Ffmpeg(new FfmpegOptions { ToolsDirectory = _dir.Path, Path = "" }, package,
            new HttpClient(new FixedResponseHandler(FakeFfmpegZip())));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ffmpeg.EnsureAvailableAsync(TestContext.Current.CancellationToken));

        Assert.Contains("integrity check", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path)); // nothing kept
    }
}

/// <summary>
/// Runs the real FFmpeg. Skipped unless TTSFREEUSE_FFMPEG points at an ffmpeg
/// executable (for example the one TTSFreeUse downloads to
/// %LOCALAPPDATA%\TTSFreeUse\tools\ffmpeg-9.0.2\ffmpeg.exe).
/// </summary>
public class FfmpegIntegrationTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(AudioFormat.Mp3)]
    [InlineData(AudioFormat.M4b)]
    public async Task EncodesNarrationToCompressedAudio(AudioFormat format)
    {
        var exe = Environment.GetEnvironmentVariable("TTSFREEUSE_FFMPEG");
        Assert.SkipWhen(string.IsNullOrEmpty(exe) || !File.Exists(exe),
            "Set TTSFREEUSE_FFMPEG to an ffmpeg executable to run this test.");
        var ct = TestContext.Current.CancellationToken;

        // Two seconds of a 440 Hz tone at 24 kHz.
        var wav = Path.Combine(_dir.Path, "in.wav");
        await using (var writer = new WavWriter(File.Create(wav), 24000))
            await writer.WriteAsync(Enumerable.Range(0, 48000).Select(i => 0.3f * MathF.Sin(i * 2 * MathF.PI * 440 / 24000)).ToArray(), ct);

        var output = Path.Combine(_dir.Path, "out" + format.Extension());
        await new Ffmpeg(new FfmpegOptions { Path = exe }).EncodeAsync(wav, output, format,
            new AudioMetadata("Test Title", "comment"), ct);

        var bytes = await File.ReadAllBytesAsync(output, ct);
        Assert.InRange(bytes.Length, 5_000, 40_000); // ~64 kbps x 2 s, far smaller than the 96 KB WAV
        if (format == AudioFormat.Mp3)
            Assert.Equal("ID3", System.Text.Encoding.ASCII.GetString(bytes, 0, 3));
        else
            Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));
    }
}

public class NarrationJobQueueTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private sealed class FakeEngine : ITtsEngine
    {
        public Func<string, Task>? OnSynthesize { get; set; }
        public string Name => "fake";
        public int SampleRate => 100;
        public IReadOnlyList<TtsVoice> Voices { get; } = new[] { new TtsVoice("v", "V", "en-US", "female") };
        public string DefaultVoice => "v";
        public bool IsReady => true;
        public string Status => "Ready.";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<float[]> SynthesizeAsync(string text, TtsOptions options, CancellationToken ct = default)
        {
            if (OnSynthesize != null)
                await OnSynthesize(text);
            ct.ThrowIfCancellationRequested();
            return new float[100]; // one second
        }
    }

    private (NarrationJobQueue Queue, AudioLibrary Library) Create(FakeEngine engine, bool ffmpegInstalled = false)
    {
        var pipeline = new NarrationPipeline(engine, new TextCleaner(), new TextChunker { MaxChunkLength = 25 });
        var library = new AudioLibrary(Path.Combine(_dir.Path, "library"));
        // A stand-in file is enough for "installed": these tests never run it.
        var ffmpegPath = Path.Combine(_dir.Path, "ffmpeg.exe");
        if (ffmpegInstalled)
            File.WriteAllText(ffmpegPath, "");
        var ffmpeg = new Ffmpeg(new FfmpegOptions
        {
            Path = ffmpegPath,
            AllowDownload = false,
            ToolsDirectory = Path.Combine(_dir.Path, "tools"),
        });
        return (new NarrationJobQueue(pipeline, library, ffmpeg), library);
    }

    private const string ThreeSentences = "First sentence here. Second sentence here. Third one here.";

    [Fact]
    public async Task FinishedJobsLetGoOfTheDocumentText()
    {
        var (queue, _) = Create(new FakeEngine());
        var job = queue.Enqueue("Doc", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Empty(job.Chunks);
    }

    [Fact]
    public void RefusesTooManyWaitingJobs()
    {
        var (queue, _) = Create(new FakeEngine());
        for (int i = 0; i < NarrationJobQueue.MaxActiveJobs; i++)
            queue.Enqueue($"Doc {i}", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        var ex = Assert.Throws<ArgumentException>(() =>
            queue.Enqueue("One too many", ThreeSentences, new TtsOptions(), AudioFormat.Wav));
        Assert.Contains("already", ex.Message);
    }

    [Fact]
    public async Task WavJobNarratesAndSavesToLibrary()
    {
        var (queue, library) = Create(new FakeEngine());
        var job = queue.Enqueue("Chapter One", ThreeSentences, new TtsOptions(), AudioFormat.Wav);
        Assert.Equal(JobState.Queued, job.State);
        Assert.Equal(3, job.ChunksTotal);

        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("Chapter One.wav", job.FileName);
        Assert.Equal(3, job.ChunksDone);
        Assert.Equal(1.0, job.Progress);
        Assert.Equal(TimeSpan.FromSeconds(3.5), job.AudioDuration); // 3 chunks + 2 gaps
        var item = Assert.Single(library.List());
        Assert.Equal("Chapter One.wav", item.FileName);
        Assert.Equal(44 + 350 * 2, item.SizeBytes);
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), $"ttsfreeuse-{job.Id:N}*"));
    }

    [Fact]
    public void RejectsCompressedFormatWhenFfmpegCantBeObtained()
    {
        var (queue, _) = Create(new FakeEngine(), ffmpegInstalled: false);

        var ex = Assert.Throws<ArgumentException>(() =>
            queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Mp3));
        Assert.Contains("FFmpeg", ex.Message);
        Assert.Empty(queue.List());
    }

    [Fact]
    public void RejectsTextWithNothingToRead()
    {
        var (queue, _) = Create(new FakeEngine());
        Assert.Throws<ArgumentException>(() => queue.Enqueue("T", "  \n\n ", new TtsOptions(), AudioFormat.Wav));
    }

    [Fact]
    public async Task CancellingDuringNarrationStopsAndLeavesNoFile()
    {
        var engine = new FakeEngine();
        var (queue, library) = Create(engine);
        NarrationJob? job = null;
        engine.OnSynthesize = _ => { if (job!.ChunksDone == 1) queue.Cancel(job.Id); return Task.CompletedTask; };
        job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Empty(library.List());
    }

    [Fact]
    public async Task CancellingAQueuedJobSkipsIt()
    {
        var (queue, library) = Create(new FakeEngine());
        var job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        Assert.True(queue.Cancel(job.Id));
        Assert.Equal(JobState.Cancelled, job.State);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.RunAsync(cts.Token));
        Assert.Empty(library.List());
    }

    [Fact]
    public async Task EngineFailureMarksJobFailedWithMessage()
    {
        var engine = new FakeEngine { OnSynthesize = _ => throw new InvalidOperationException("engine broke") };
        var (queue, library) = Create(engine);
        var job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("engine broke", job.Error);
        Assert.Empty(library.List());
    }

    [Fact]
    public async Task AJobCancelledJustBeforeItStartsIsNotNarrated()
    {
        // The worker has taken the job from the queue when the user cancels it.
        var (queue, library) = Create(new FakeEngine());
        var job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Wav);
        Assert.True(queue.Cancel(job.Id));

        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Null(job.FileName);
        Assert.Empty(library.List());
    }

    [Fact]
    public void RejectsAnUnknownFormat()
    {
        var (queue, _) = Create(new FakeEngine(), ffmpegInstalled: true);

        var ex = Assert.Throws<ArgumentException>(() =>
            queue.Enqueue("T", ThreeSentences, new TtsOptions(), (AudioFormat)7));
        Assert.Contains("format", ex.Message);
        Assert.Empty(queue.List());
    }

    [Fact]
    public void TidiesLongAndOddTitles()
    {
        var (queue, _) = Create(new FakeEngine());

        var long_ = queue.Enqueue(new string('x', 5000), ThreeSentences, new TtsOptions(), AudioFormat.Wav);
        var odd = queue.Enqueue(" Line one\nLine\ttwo\0 ", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        Assert.Equal(200, long_.Title.Length);
        Assert.Equal("Line one Line two", odd.Title);
    }

    /// <summary>A download that waits until the test lets it fail.</summary>
    private sealed class HeldDownloadHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await _release.Task.WaitAsync(ct);
            throw new HttpRequestException("Offline (test).");
        }
    }

    [Fact]
    public async Task CancellingWhileTheAudioConverterDownloadsStopsRightAway()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Automatic download is Windows-only.");
        var ct = TestContext.Current.CancellationToken;
        var pipeline = new NarrationPipeline(new FakeEngine(), new TextCleaner(), new TextChunker { MaxChunkLength = 25 });
        var download = new HeldDownloadHandler();
        var ffmpeg = new Ffmpeg(
            new FfmpegOptions { Path = Path.Combine(_dir.Path, "no-ffmpeg"), ToolsDirectory = Path.Combine(_dir.Path, "tools") },
            new FfmpegPackage("1.0", new Uri("https://example.test/ffmpeg.zip"), new string('0', 64)),
            new HttpClient(download));
        var library = new AudioLibrary(Path.Combine(_dir.Path, "library"));
        var queue = new NarrationJobQueue(pipeline, library, ffmpeg);

        // Queuing an MP3 starts fetching the converter, which is held up here.
        var job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Mp3);
        var processing = queue.ProcessAsync(job, ct);
        while (job.State != JobState.Encoding)
            await Task.Delay(10, ct);

        queue.Cancel(job.Id);
        await processing.WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Empty(library.List());

        // Let the download end before the test folder is deleted.
        download.Release();
        await Assert.ThrowsAsync<HttpRequestException>(() => ffmpeg.EnsureAvailableAsync(ct));
    }

    [Fact]
    public async Task FinishedJobsCanBeDismissedButActiveOnesCannot()
    {
        var (queue, _) = Create(new FakeEngine());
        var job = queue.Enqueue("T", ThreeSentences, new TtsOptions(), AudioFormat.Wav);

        Assert.False(queue.Dismiss(job.Id));
        await queue.ProcessAsync(job, TestContext.Current.CancellationToken);
        Assert.True(queue.Dismiss(job.Id));
        Assert.Empty(queue.List());
    }
}
