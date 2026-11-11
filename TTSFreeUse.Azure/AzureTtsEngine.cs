using System.Buffers.Binary;
using Microsoft.CognitiveServices.Speech;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Azure
{
    /// <summary>
    /// Optional cloud engine using Azure AI Speech. Requires an Azure account,
    /// key, and region; Kokoro is the free offline default.
    /// </summary>
    public class AzureTtsEngine : ITtsEngine
    {
        private readonly string _key;
        private readonly string _region;

        // A small curated set; any Azure neural voice name is also accepted.
        private static readonly IReadOnlyList<TtsVoice> VoiceList = new List<TtsVoice>
        {
            new("en-US-JennyNeural", "Jenny", "en-US", "female"),
            new("en-US-AriaNeural", "Aria", "en-US", "female"),
            new("en-US-GuyNeural", "Guy", "en-US", "male"),
            new("en-US-DavisNeural", "Davis", "en-US", "male"),
            new("en-GB-SoniaNeural", "Sonia", "en-GB", "female"),
            new("en-GB-RyanNeural", "Ryan", "en-GB", "male"),
        };

        public AzureTtsEngine(string? key, string? region)
        {
            // Fail with a clear message instead of an opaque SDK error on the
            // first synthesis call.
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
                throw new InvalidOperationException(
                    "Azure Speech is not configured. Set Azure:SpeechKey and " +
                    "Azure:SpeechRegion (see README.md), or use the free Kokoro engine.");

            _key = key;
            _region = region;
        }

        public string Name => "azure";
        public int SampleRate => 24000;
        public IReadOnlyList<TtsVoice> Voices => VoiceList;
        public string DefaultVoice => "en-US-JennyNeural";
        public bool IsReady => true;
        public string Status => "Ready.";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<float[]> SynthesizeAsync(string text, TtsOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!TtsOptions.IsValidSpeed(options.Speed))
                throw new ArgumentOutOfRangeException(nameof(options), options.Speed,
                    $"Speed must be between {TtsOptions.MinSpeed} and {TtsOptions.MaxSpeed}.");

            var config = SpeechConfig.FromSubscription(_key, _region);
            config.SpeechSynthesisVoiceName = options.Voice ?? DefaultVoice;
            config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);

            using var synthesizer = new SpeechSynthesizer(config, null);
            using var registration = cancellationToken.Register(() => _ = synthesizer.StopSpeakingAsync());

            var result = options.Speed == 1.0f
                ? await synthesizer.SpeakTextAsync(text)
                : await synthesizer.SpeakSsmlAsync(BuildSsml(text, config.SpeechSynthesisVoiceName, options.Speed));
            cancellationToken.ThrowIfCancellationRequested();

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new Exception($"Azure TTS failed: {cancellation.ErrorDetails}");
            }

            return Pcm16ToFloat(result.AudioData);
        }

        private static string BuildSsml(string text, string voice, float speed)
        {
            // Both are escaped, so text can't add its own SSML instructions.
            var escapedText = System.Security.SecurityElement.Escape(text);
            var escapedVoice = System.Security.SecurityElement.Escape(voice);
            var rate = ((speed - 1.0f) * 100).ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture);
            return "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">" +
                   $"<voice name=\"{escapedVoice}\"><prosody rate=\"{rate}%\">{escapedText}</prosody></voice></speak>";
        }

        private static float[] Pcm16ToFloat(byte[] pcm)
        {
            var samples = new float[pcm.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) / 32768f;
            return samples;
        }
    }
}
