using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Api.Controllers
{
    /// <summary>
    /// Live listening: splits text into short passages and speaks one passage
    /// at a time as WAV audio. Documents to keep go through /api/jobs instead.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SpeechController : ControllerBase
    {
        /// <summary>Largest document upload accepted (also used by /api/jobs).</summary>
        internal const long MaxUploadBytes = 200 * 1024 * 1024;

        /// <summary>
        /// Most text spoken in one request. The page sends ~300-character
        /// passages; longer text should be saved as an audio file instead.
        /// </summary>
        internal const int MaxSpeakCharacters = 10_000;

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

        /// <summary>Speaks a short piece of text and returns it as WAV audio.</summary>
        [HttpPost("text")]
        public async Task<IActionResult> Text([FromBody] TextRequest request)
        {
            var speed = request.Speed ?? 1.0f;
            var problem = RequestValidation.Check(Engine, request.Voice, speed);
            if (problem != null)
                return problem;
            if (request.Text?.Length > MaxSpeakCharacters)
                return BadRequest($"That's more than {MaxSpeakCharacters:N0} characters. " +
                                  "Use \"Save audio file\" for longer text.");

            var chunks = _pipeline.Prepare(request.Text ?? string.Empty);
            if (chunks.Count == 0)
                return UnprocessableEntity(
                    "No readable text found. Scanned PDFs (images of pages) aren't supported yet.");

            // Write to a temp file rather than memory: long text is a lot of audio.
            var output = TempAudioFile.Create($"{Guid.NewGuid():N}.wav", FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            try
            {
                // Stops synthesizing if the user closes the page or cancels.
                await _pipeline.NarrateAsync(chunks, output, new TtsOptions(request.Voice, speed),
                    cancellationToken: HttpContext.RequestAborted);
                output.Position = 0;
                return File(output, "audio/wav");
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

    internal static partial class RequestValidation
    {
        // Voice ids are short names: "af_heart", "en-US-JennyNeural".
        [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
        private static partial Regex VoiceId();

        /// <summary>Returns an error response for invalid voice/speed or an engine that isn't ready.</summary>
        public static IActionResult? Check(ITtsEngine engine, string? voice, float speed)
        {
            if (!engine.IsReady)
                return new ObjectResult(engine.Status) { StatusCode = StatusCodes.Status503ServiceUnavailable };

            if (!TtsOptions.IsValidSpeed(speed))
                return new BadRequestObjectResult(
                    $"Speed must be a number from {TtsOptions.MinSpeed} to {TtsOptions.MaxSpeed}.");

            if (voice != null && !VoiceId().IsMatch(voice))
                return new BadRequestObjectResult("Unknown voice. See GET /api/speech/voices.");
            // Azure accepts any of its neural voice names; Kokoro only its own.
            if (voice != null && engine.Name == "kokoro" && engine.Voices.All(v => v.Id != voice))
                return new BadRequestObjectResult($"Unknown voice '{voice}'. See GET /api/speech/voices.");

            return null;
        }
    }
}
