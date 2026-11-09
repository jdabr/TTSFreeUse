using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Api.Controllers
{
    /// <summary>
    /// Instant narration: returns WAV audio directly in the response. Good for
    /// short text you want to hear right away; use /api/jobs for documents.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        internal const long MaxUploadBytes = 200 * 1024 * 1024;

        private readonly NarrationPipeline _pipeline;
        private readonly DocumentReader _reader;

        public SpeechController(NarrationPipeline pipeline, DocumentReader reader)
        {
            _pipeline = pipeline;
            _reader = reader;
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

        // Short enough that a chunk is synthesized in a few seconds, long
        // enough to hold a sentence or two so the voice keeps its flow.
        internal const int ListenChunkLength = 300;

        /// <summary>
        /// Splits pasted text into short passages for live listening. The page
        /// then requests audio for each passage with POST /api/speech/text and
        /// plays them in order, so speech starts after the first passage.
        /// </summary>
        [HttpPost("prepare")]
        [RequestSizeLimit(MaxUploadBytes)]
        public IActionResult Prepare([FromBody] TextRequest request) =>
            Prepared(request.Text, "Pasted text");

        /// <summary>Like <c>prepare</c>, for an uploaded PDF, DOCX, or TXT file.</summary>
        [HttpPost("prepare-file")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<IActionResult> PrepareFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");
            try
            {
                await using var stream = file.OpenReadStream();
                var text = await _reader.ReadAsync(stream, file.FileName);
                return Prepared(text, Path.GetFileNameWithoutExtension(file.FileName));
            }
            catch (DocumentReadException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private IActionResult Prepared(string? rawText, string title)
        {
            var passages = _pipeline.Prepare(rawText ?? string.Empty, ListenChunkLength);
            return passages.Count == 0
                ? UnprocessableEntity("No readable text found. Scanned PDFs (images of pages) aren't supported yet.")
                : Ok(new PreparedText(title, passages));
        }

        /// <summary>Narrates an uploaded PDF, DOCX, or TXT file and returns a WAV file.</summary>
        [HttpPost("convert")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<IActionResult> Convert(IFormFile file,
            [FromForm] string? voice, [FromForm] float speed = 1.0f)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            string rawText;
            try
            {
                await using var stream = file.OpenReadStream();
                rawText = await _reader.ReadAsync(stream, file.FileName);
            }
            catch (DocumentReadException ex)
            {
                return BadRequest(ex.Message);
            }

            return await NarrateAsync(rawText, voice, speed, Path.GetFileNameWithoutExtension(file.FileName));
        }

        /// <summary>Narrates text typed or pasted by the user and returns a WAV file.</summary>
        [HttpPost("text")]
        [RequestSizeLimit(MaxUploadBytes)]
        public Task<IActionResult> Text([FromBody] TextRequest request) =>
            NarrateAsync(request.Text, request.Voice, request.Speed ?? 1.0f, "narration");

        private async Task<IActionResult> NarrateAsync(string? rawText, string? voice, float speed, string fileName)
        {
            var problem = RequestValidation.Check(Engine, voice, speed);
            if (problem != null)
                return problem;

            var chunks = _pipeline.Prepare(rawText ?? string.Empty);
            if (chunks.Count == 0)
                return UnprocessableEntity(
                    "No readable text found. Scanned PDFs (images of pages) aren't supported yet.");

            // Write to a temp file rather than memory: long text is a lot of audio.
            var path = Path.Combine(Path.GetTempPath(), $"ttsfreeuse-{Guid.NewGuid():N}.wav");
            var output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                // Stops synthesizing if the user closes the page or cancels.
                await _pipeline.NarrateAsync(chunks, output, new TtsOptions(voice, speed),
                    cancellationToken: HttpContext.RequestAborted);
                output.Position = 0;
                return File(output, "audio/wav", Core.Jobs.AudioLibrary.SafeName(fileName) + ".wav");
            }
            catch
            {
                await output.DisposeAsync(); // deletes the temp file
                throw;
            }
        }
    }

    public record TextRequest(string Text, string? Voice = null, float? Speed = null);

    public record PreparedText(string Title, IReadOnlyList<string> Passages);

    internal static class RequestValidation
    {
        /// <summary>Returns an error response for invalid voice/speed or an engine that isn't ready.</summary>
        public static IActionResult? Check(ITtsEngine engine, string? voice, float speed)
        {
            if (!engine.IsReady)
                return new ObjectResult(engine.Status) { StatusCode = StatusCodes.Status503ServiceUnavailable };

            if (speed is < 0.5f or > 2.0f)
                return new BadRequestObjectResult("Speed must be between 0.5 and 2.0.");

            // Azure accepts any of its neural voice names; Kokoro only its own.
            if (voice != null && engine.Name == "kokoro" && engine.Voices.All(v => v.Id != voice))
                return new BadRequestObjectResult($"Unknown voice '{voice}'. See GET /api/speech/voices.");

            return null;
        }
    }
}
