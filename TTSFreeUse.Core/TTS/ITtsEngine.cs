namespace TTSFreeUse.Core.TTS
{
    /// <summary>
    /// A text-to-speech engine. Engines return raw mono PCM samples in the
    /// range [-1, 1] at <see cref="SampleRate"/>, so audio from any engine can
    /// be written, concatenated, and encoded the same way.
    /// </summary>
    public interface ITtsEngine
    {
        /// <summary>Short identifier, e.g. "kokoro" or "azure".</summary>
        string Name { get; }

        int SampleRate { get; }

        IReadOnlyList<TtsVoice> Voices { get; }

        /// <summary>Voice used when a request doesn't specify one.</summary>
        string DefaultVoice { get; }

        /// <summary>
        /// False while the engine is still preparing (e.g. downloading its
        /// voice model). <see cref="Status"/> explains why.
        /// </summary>
        bool IsReady { get; }

        string Status { get; }

        /// <summary>
        /// Prepares the engine (download/load models). Called at startup so the
        /// first request doesn't wait; synthesis also calls it if needed.
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        Task<float[]> SynthesizeAsync(string text, TtsOptions options,
            CancellationToken cancellationToken = default);
    }

    public record TtsVoice(string Id, string Name, string Language, string Gender);

    /// <param name="Voice">Voice id from <see cref="ITtsEngine.Voices"/>; null for the default.</param>
    /// <param name="Speed">Speaking rate multiplier, 1.0 = normal.</param>
    public record TtsOptions(string? Voice = null, float Speed = 1.0f);
}
