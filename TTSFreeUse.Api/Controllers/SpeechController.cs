using Microsoft.AspNetCore.Mvc;
using DocToSpeech.Core.Extractors;
using DocToSpeech.Core.Transformers;
using DocToSpeech.Core.TTS;

namespace DocToSpeech.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        private readonly AzureSpeechService _tts;
        private readonly TextCleaner _cleaner;
        private readonly TextChunker _chunker;
        private readonly IEnumerable<ITextExtractor> _extractors;

        public SpeechController(
            AzureSpeechService tts,
            TextCleaner cleaner,
            TextChunker chunker,
            IEnumerable<ITextExtractor> extractors)
        {
            _tts = tts;
            _cleaner = cleaner;
            _chunker = chunker;
            _extractors = extractors;
        }

        [HttpPost("convert")]
        public async Task<IActionResult> Convert(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // Get file extension
            var ext = Path.GetExtension(file.FileName).ToLower();

            // Find the right extractor
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(ext));
            if (extractor == null)
                return BadRequest($"Unsupported file type: {ext}");

            // 1. Extract text
            using var stream = file.OpenReadStream();
            var rawText = await extractor.ExtractAsync(stream);

            // 2. Clean text
            var cleanText = _cleaner.Clean(rawText);

            // 3. Chunk text
            var chunks = _chunker.Chunk(cleanText);

            // 4. Synthesize each chunk and combine audio
            var audioSegments = new List<byte[]>();
            foreach (var chunk in chunks)
            {
                var audio = await _tts.SynthesizeAsync(chunk);
                audioSegments.Add(audio);
            }

            // 5. Combine all audio bytes
            var combined = audioSegments.SelectMany(a => a).ToArray();

            return File(combined, "audio/mpeg", "output.mp3");
        }
    }
}
