using System.Buffers.Binary;
using System.Text;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.Transformers;
using TTSFreeUse.Core.TTS;
using TTSFreeUse.Core.TTS.Kokoro;

namespace TTSFreeUse.Core.Tests;

public class WavWriterTests
{
    [Fact]
    public async Task WritesValidPcmHeader()
    {
        var ms = new MemoryStream();
        await using (var wav = new WavWriter(ms, 24000, leaveOpen: true))
            await wav.WriteAsync(new float[] { 0f, 0.5f, -0.5f, 1f }, TestContext.Current.CancellationToken);

        var b = ms.ToArray();
        Assert.Equal("RIFF", Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(b, 8, 4));
        Assert.Equal((uint)(b.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(20)));       // PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(22)));       // mono
        Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(34)));      // bits
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(40)));     // data bytes
        Assert.Equal(52, b.Length);
    }

    [Fact]
    public async Task ConvertsAndClampsSamples()
    {
        var ms = new MemoryStream();
        await using (var wav = new WavWriter(ms, 8000, leaveOpen: true))
            await wav.WriteAsync(new float[] { 1f, -1f, 2f, -3f, 0f }, TestContext.Current.CancellationToken);

        var b = ms.ToArray();
        short S(int i) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(44 + i * 2));
        Assert.Equal(new short[] { 32767, -32767, 32767, -32767, 0 }, Enumerable.Range(0, 5).Select(S));
    }

    [Fact]
    public async Task TracksDurationIncludingSilence()
    {
        var ms = new MemoryStream();
        await using var wav = new WavWriter(ms, 1000, leaveOpen: true);
        await wav.WriteAsync(new float[500], TestContext.Current.CancellationToken);
        await wav.WriteSilenceAsync(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMilliseconds(750), wav.Duration);
    }

    [Fact]
    public void RejectsNonSeekableStreams()
    {
        Assert.Throws<ArgumentException>(() => new WavWriter(new NonSeekableStream(), 24000));
    }

    private class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}

public class NarrationPipelineTests
{
    /// <summary>Engine that returns one second of audio per chunk, without any model.</summary>
    private class FakeEngine : ITtsEngine
    {
        public List<(string Text, TtsOptions Options)> Calls { get; } = new();
        public string Name => "fake";
        public int SampleRate => 100;
        public IReadOnlyList<TtsVoice> Voices { get; } = new[] { new TtsVoice("v", "V", "en-US", "female") };
        public string DefaultVoice => "v";
        public bool IsReady => true;
        public string Status => "Ready.";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<float[]> SynthesizeAsync(string text, TtsOptions options, CancellationToken ct = default)
        {
            Calls.Add((text, options));
            return Task.FromResult(Enumerable.Repeat(0.1f, 100).ToArray());
        }
    }

    private static NarrationPipeline Pipeline(FakeEngine engine, int maxChunk = 800) =>
        new(engine, new TextCleaner(), new TextChunker { MaxChunkLength = maxChunk });

    [Fact]
    public async Task SynthesizesEachChunkWithGapsBetween()
    {
        var engine = new FakeEngine();
        var pipeline = Pipeline(engine, maxChunk: 25);
        var chunks = pipeline.Prepare("First sentence here. Second sentence here. Third one here.");

        var progress = new List<NarrationProgress>();
        var duration = await pipeline.NarrateAsync(chunks, new MemoryStream(), new TtsOptions("v", 1.5f),
            new SyncProgress<NarrationProgress>(progress.Add), TestContext.Current.CancellationToken);

        Assert.Equal(3, engine.Calls.Count);
        Assert.All(engine.Calls, c => Assert.Equal(new TtsOptions("v", 1.5f), c.Options));
        // 3 chunks x 1s + 2 gaps x 0.25s
        Assert.Equal(TimeSpan.FromSeconds(3.5), duration);
        Assert.Equal(new[] { 1, 2, 3 }, progress.Select(p => p.ChunksDone));
        Assert.Equal(1.0, progress[^1].Fraction);
    }

    [Fact]
    public void PrepareCleansBeforeChunking()
    {
        var chunks = Pipeline(new FakeEngine()).Prepare("The compu-\nter “works”.");
        Assert.Equal(new[] { "The computer \"works\"." }, chunks);
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        var engine = new FakeEngine();
        var pipeline = Pipeline(engine, maxChunk: 25);
        var chunks = pipeline.Prepare("First sentence here. Second sentence here. Third one here.");
        using var cts = new CancellationTokenSource();

        var progress = new SyncProgress<NarrationProgress>(p => { if (p.ChunksDone == 1) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.NarrateAsync(chunks, new MemoryStream(), new TtsOptions(), progress, cts.Token));
        Assert.Single(engine.Calls);
    }

    [Fact]
    public async Task DoesNothingWhenAlreadyCancelled()
    {
        var engine = new FakeEngine();
        var output = new MemoryStream();

        // Even with nothing to read, a cancelled narration mustn't "succeed".
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Pipeline(engine).NarrateAsync(Array.Empty<string>(), output, new TtsOptions(),
                cancellationToken: new CancellationToken(canceled: true)));
        Assert.Empty(engine.Calls);
        Assert.Equal(0, output.Length);
    }

    /// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts asynchronously).</summary>
    private class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public class KokoroEngineTests
{
    [Fact]
    public void ListsOnlyTheRecommendedVoicesByDefault()
    {
        using var engine = new KokoroTtsEngine(new KokoroOptions { ModelsDirectory = Path.GetTempPath() });

        Assert.Equal(KokoroTtsEngine.RecommendedVoiceIds, engine.Voices.Select(v => v.Id));
        Assert.Contains(engine.Voices, v => v is { Id: "af_heart", Name: "Heart", Language: "en-US", Gender: "female" });
        Assert.Contains(engine.Voices, v => v is { Id: "bm_george", Name: "George", Language: "en-GB", Gender: "male" });
        Assert.DoesNotContain(engine.Voices, v => v.Id == "am_adam"); // graded F+
        Assert.False(engine.IsReady);
    }

    [Fact]
    public void CanListAllEnglishVoices()
    {
        using var engine = new KokoroTtsEngine(new KokoroOptions
        {
            ModelsDirectory = Path.GetTempPath(),
            ShowAllVoices = true,
        });

        Assert.Equal(28, engine.Voices.Count);
    }

    [Fact]
    public void ListsAConfiguredDefaultOutsideTheRecommendedSet()
    {
        using var engine = new KokoroTtsEngine(new KokoroOptions
        {
            ModelsDirectory = Path.GetTempPath(),
            DefaultVoice = "bm_lewis",
        });

        Assert.Equal(KokoroTtsEngine.RecommendedVoiceIds.Length + 1, engine.Voices.Count);
        Assert.Contains(engine.Voices, v => v.Id == "bm_lewis");
    }

    [Fact]
    public void RejectsUnknownDefaultVoice()
    {
        Assert.Throws<ArgumentException>(() => new KokoroTtsEngine(new KokoroOptions { DefaultVoice = "nope" }));
    }

    [Theory]
    [InlineData(float.NaN)] // made the native engine throw an SEHException
    [InlineData(0.1f)]
    [InlineData(5f)]
    public async Task RefusesUnusableSpeedsBeforeLoadingAnything(float speed)
    {
        using var dir = new TempDir();
        using var engine = new KokoroTtsEngine(new KokoroOptions { ModelsDirectory = dir.Path });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            engine.SynthesizeAsync("Hello.", new TtsOptions(Speed: speed), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path)); // no model download was started
    }

    [Theory]
    [InlineData(float.NaN, false)]
    [InlineData(float.NegativeInfinity, false)]
    [InlineData(0.49f, false)]
    [InlineData(0.5f, true)]
    [InlineData(1.0f, true)]
    [InlineData(2.0f, true)]
    [InlineData(2.01f, false)]
    public void OnlyUsableSpeedsAreValid(float speed, bool valid) =>
        Assert.Equal(valid, TtsOptions.IsValidSpeed(speed));

    /// <summary>
    /// Quitting while TTSFreeUse is reading aloud used to free the voice engine
    /// in the middle of speech, crashing the app (access violation). Runs only
    /// with TTSFREEUSE_MODELS_DIR set, like <see cref="SynthesizesSpeech"/>.
    /// </summary>
    [Fact]
    public async Task QuittingWhileSpeakingDoesNotCrash()
    {
        var dir = Environment.GetEnvironmentVariable("TTSFREEUSE_MODELS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(dir) || !new KokoroModel(dir).IsInstalled(),
            "Set TTSFREEUSE_MODELS_DIR to a folder containing the Kokoro model to run this test.");
        var ct = TestContext.Current.CancellationToken;

        var engine = new KokoroTtsEngine(new KokoroOptions { ModelsDirectory = dir });
        await engine.InitializeAsync(ct);
        var longText = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog by the river.", 12));
        var speaking = engine.SynthesizeAsync(longText, new TtsOptions(), ct);
        await Task.Delay(1500, ct); // well into the native call

        engine.Dispose();

        Assert.NotEmpty(await speaking); // the speech in progress finishes normally
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.SynthesizeAsync("Hi.", new TtsOptions(), ct));
    }

    /// <summary>
    /// Real synthesis. Runs only when TTSFREEUSE_MODELS_DIR points at a folder
    /// that already contains the downloaded Kokoro model (avoids a 330 MB
    /// download during normal test runs).
    /// </summary>
    [Fact]
    public async Task SynthesizesSpeech()
    {
        var dir = Environment.GetEnvironmentVariable("TTSFREEUSE_MODELS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(dir) || !new KokoroModel(dir).IsInstalled(),
            "Set TTSFREEUSE_MODELS_DIR to a folder containing the Kokoro model to run this test.");

        using var engine = new KokoroTtsEngine(new KokoroOptions { ModelsDirectory = dir });
        var samples = await engine.SynthesizeAsync("Hello world.", new TtsOptions("bf_emma"),
            TestContext.Current.CancellationToken);

        Assert.InRange(samples.Length / 24000.0, 0.3, 5.0);
        Assert.True(samples.Any(s => Math.Abs(s) > 0.05f), "Audio should not be silent.");
    }
}
