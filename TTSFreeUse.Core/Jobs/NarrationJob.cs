using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Core.Jobs
{
    public enum JobState { Queued, Narrating, Encoding, Completed, Failed, Cancelled }

    /// <summary>A document being turned into an audio file in the background.</summary>
    public class NarrationJob
    {
        internal NarrationJob(string title, IReadOnlyList<string> chunks, TtsOptions options, AudioFormat format)
        {
            Title = title;
            Chunks = chunks;
            Options = options;
            Format = format;
            ChunksTotal = chunks.Count;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public string Title { get; }
        public AudioFormat Format { get; }
        public TtsOptions Options { get; }
        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;

        // Updated by the worker; read by the UI. Individual fields may be a
        // moment out of date relative to each other, which is fine for display.
        public JobState State { get; internal set; } = JobState.Queued;
        public string? StatusText { get; internal set; }
        public int ChunksDone { get; internal set; }
        public int ChunksTotal { get; }
        public DateTimeOffset? Started { get; internal set; }
        public TimeSpan? AudioDuration { get; internal set; }
        public string? FileName { get; internal set; }
        public string? Error { get; internal set; }

        public bool IsActive => State is JobState.Queued or JobState.Narrating or JobState.Encoding;

        /// <summary>Overall progress 0–1; narration is most of the work.</summary>
        public double Progress => State switch
        {
            JobState.Completed => 1,
            JobState.Encoding => 0.97,
            _ => ChunksTotal == 0 ? 0 : 0.95 * ChunksDone / ChunksTotal,
        };

        /// <summary>Estimated time left, once a few chunks have been timed.</summary>
        public TimeSpan? EstimatedRemaining
        {
            get
            {
                if (State != JobState.Narrating || Started == null || ChunksDone < 2)
                    return null;
                var elapsed = DateTimeOffset.UtcNow - Started.Value;
                var perChunk = elapsed / ChunksDone;
                return perChunk * (ChunksTotal - ChunksDone);
            }
        }

        /// <summary>The prepared text; released once the job ends so finished jobs don't hold whole documents in memory.</summary>
        internal IReadOnlyList<string> Chunks { get; private set; }
        internal CancellationTokenSource Cancellation { get; } = new();

        internal void ReleaseText() => Chunks = Array.Empty<string>();
    }
}
