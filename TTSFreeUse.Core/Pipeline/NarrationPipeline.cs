using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Transformers;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Core.Pipeline
{
    /// <summary>
    /// Turns text into narrated audio: clean → chunk → synthesize → WAV.
    /// Shared by every front end (web API today; desktop app/CLI later).
    /// </summary>
    public class NarrationPipeline
    {
        // A short pause between chunks so sentences don't run into each other.
        private static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(250);

        private readonly ITtsEngine _engine;
        private readonly TextCleaner _cleaner;
        private readonly TextChunker _chunker;

        public NarrationPipeline(ITtsEngine engine, TextCleaner cleaner, TextChunker chunker)
        {
            _engine = engine;
            _cleaner = cleaner;
            _chunker = chunker;
        }

        public ITtsEngine Engine => _engine;

        /// <summary>
        /// Cleans and chunks <paramref name="rawText"/>. Returns no chunks when
        /// the text has nothing speakable (e.g. a scanned PDF with no text layer).
        /// </summary>
        public List<string> Prepare(string rawText) => _chunker.Chunk(_cleaner.Clean(rawText));

        /// <summary>
        /// Like <see cref="Prepare(string)"/> but with a custom chunk size. Live
        /// listening uses short chunks so the first audio is ready in seconds.
        /// </summary>
        public List<string> Prepare(string rawText, int maxChunkLength) =>
            new TextChunker { MaxChunkLength = maxChunkLength }.Chunk(_cleaner.Clean(rawText));

        /// <summary>Synthesizes prepared chunks into WAV audio written to <paramref name="output"/>.</summary>
        /// <param name="output">Writable, seekable stream; left open.</param>
        public async Task<TimeSpan> NarrateAsync(IReadOnlyList<string> chunks, Stream output,
            TtsOptions options, IProgress<NarrationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await using var wav = new WavWriter(output, _engine.SampleRate, leaveOpen: true);

            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var samples = await _engine.SynthesizeAsync(chunks[i], options, cancellationToken);
                await wav.WriteAsync(samples, cancellationToken);
                if (i < chunks.Count - 1)
                    await wav.WriteSilenceAsync(ChunkGap, cancellationToken);
                progress?.Report(new NarrationProgress(i + 1, chunks.Count));
            }

            await wav.FinishAsync();
            return wav.Duration;
        }
    }

    public record NarrationProgress(int ChunksDone, int ChunksTotal)
    {
        public double Fraction => ChunksTotal == 0 ? 1 : (double)ChunksDone / ChunksTotal;
    }
}
