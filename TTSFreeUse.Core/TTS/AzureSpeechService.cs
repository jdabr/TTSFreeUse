using System;
using Microsoft.CognitiveServices.Speech;

namespace DocToSpeech.Core.TTS
{
    public class AzureSpeechService
    {
        private readonly string _key;
        private readonly string _region;

        public AzureSpeechService(string key, string region)
        {
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