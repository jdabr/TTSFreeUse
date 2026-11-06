using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        private const long MaxUploadBytes = 200 * 1024 * 1024;

        private readonly NarrationPipeline _pipeline;
        private readonly IEnumerable<ITextExtractor> _extractors;

        public SpeechController(NarrationPipeline pipeline, IEnumerable<ITextExtractor> extractors)
        {
            _pipeline = pipeline;
            _extractors = extractors;
        }

        private ITtsEngine Engine => _pipeline.Engine;

        /// <summary>Whether the voice engine is ready (the model may still be downloading).</summary>
        [HttpGet("status")]
        public IActionResult Status() =>
            Ok(new { engine = Engine.Name, ready = Engine.IsReady, status = Engine.Status });

        /// <summary>Voices available for the current engine.</summary>
        [HttpGet("voices")]
        public IActionResult Voices() =>
            Ok(new { engine = Engine.Name, defaultVoice = Engine.DefaultVoice, voices = Engine.Voices });

        /// <summary>Narrates an uploaded PDF, DOCX, or TXT file and returns a WAV file.</summary>
        [HttpPost("convert")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<IActionResult> Convert(IFormFile file,
            [FromForm] string? voice, [FromForm] float speed = 1.0f)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(ext));
            if (extractor == null)
                return BadRequest($"Unsupported file type: {ext}. Supported: .pdf, .docx, .txt");

            string rawText;
            try
            {
                await using var stream = file.OpenReadStream();
                rawText = await extractor.ExtractAsync(stream);
            }
            catch (ProtectedDocumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Corrupt, truncated, or password-protected files land here.
                return BadRequest($"Couldn't read this {ext} file. It may be damaged or password-protected.");
            }

            var name = Path.GetFileNameWithoutExtension(file.FileName);
            return await NarrateAsync(rawText, voice, speed, name);
        }

        /// <summary>Narrates text typed or pasted by the user and returns a WAV file.</summary>
        [HttpPost("text")]
        [RequestSizeLimit(MaxUploadBytes)]
        public Task<IActionResult> Text([FromBody] TextRequest request) =>
            NarrateAsync(request.Text, request.Voice, request.Speed ?? 1.0f, "narration");

        private async Task<IActionResult> NarrateAsync(string? rawText, string? voice, float speed, string fileName)
        {
            if (!Engine.IsReady)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, Engine.Status);

            if (speed is < 0.5f or > 2.0f)
                return BadRequest("Speed must be between 0.5 and 2.0.");

            // Azure accepts any of its neural voice names; Kokoro only its own.
            if (voice != null && Engine.Name == "kokoro" && Engine.Voices.All(v => v.Id != voice))
                return BadRequest($"Unknown voice '{voice}'. See GET /api/speech/voices.");

            var chunks = _pipeline.Prepare(rawText ?? string.Empty);
            if (chunks.Count == 0)
                return UnprocessableEntity(
                    "No readable text found. Scanned PDFs (images of pages) aren't supported yet.");

            // Write to a temp file rather than memory: a whole book is hundreds of MB of audio.
            var path = Path.Combine(Path.GetTempPath(), $"ttsfreeuse-{Guid.NewGuid():N}.wav");
            var output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                // Stops synthesizing if the user closes the page or cancels.
                await _pipeline.NarrateAsync(chunks, output, new TtsOptions(voice, speed),
                    cancellationToken: HttpContext.RequestAborted);
                output.Position = 0;
                return File(output, "audio/wav", SafeFileName(fileName) + ".wav");
            }
            catch
            {
                await output.DisposeAsync(); // deletes the temp file
                throw;
            }
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return safe.Length > 0 ? safe : "narration";
        }
    }

    public record TextRequest(string Text, string? Voice = null, float? Speed = null);
}
