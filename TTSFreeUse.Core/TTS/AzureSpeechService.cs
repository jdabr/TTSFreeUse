using Microsoft.CognitiveServices.Speech;

namespace TTSFreeUse.Core.TTS
{
    public class AzureSpeechService
    {
        private readonly string _key;
        private readonly string _region;

        public AzureSpeechService(string? key, string? region)
        {
            // Fail with a clear message instead of an opaque SDK error on the
            // first synthesis call.
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
                throw new InvalidOperationException(
                    "Azure Speech is not configured. Set Azure:SpeechKey and " +
                    "Azure:SpeechRegion (see README.md).");

            _key = key;
            _region = region;
        }

        public async Task<byte[]> SynthesizeAsync(string text,
            string voice = "en-US-JennyNeural")
        {
            var config = SpeechConfig.FromSubscription(_key, _region);
            config.SpeechSynthesisVoiceName = voice;
            config.SetSpeechSynthesisOutputFormat(
                SpeechSynthesisOutputFormat.Audio16Khz128KBitRateMonoMp3);

            using var synthesizer = new SpeechSynthesizer(config, null);
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new Exception($"TTS failed: {cancellation.ErrorDetails}");
            }

            return result.AudioData;
        }
    }
}
