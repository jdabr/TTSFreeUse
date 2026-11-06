using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Jobs;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.TTS;

namespace TTSFreeUse.Api.Controllers
{
    /// <summary>
    /// Background narration of documents into audio files (MP3, M4B, WAV).
    /// Finished files are saved to the library.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class JobsController : ControllerBase
    {
        private readonly NarrationJobQueue _queue;
        private readonly DocumentReader _reader;
        private readonly ITtsEngine _engine;

        public JobsController(NarrationJobQueue queue, DocumentReader reader, NarrationPipeline pipeline)
        {
            _queue = queue;
            _reader = reader;
            _engine = pipeline.Engine;
        }

        /// <summary>Jobs from this session, newest first.</summary>
        [HttpGet]
        public IActionResult List() => Ok(_queue.List().Select(JobDto.From));

        [HttpGet("{id:guid}")]
        public IActionResult Get(Guid id) =>
            _queue.Get(id) is { } job ? Ok(JobDto.From(job)) : NotFound();

        /// <summary>Queues an uploaded PDF, DOCX, or TXT file for narration.</summary>
        [HttpPost("file")]
        [RequestSizeLimit(SpeechController.MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = SpeechController.MaxUploadBytes)]
        public async Task<IActionResult> FromFile(IFormFile file, [FromForm] string? voice,
            [FromForm] float speed = 1.0f, [FromForm] AudioFormat format = AudioFormat.Mp3)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var problem = RequestValidation.Check(_engine, voice, speed);
            if (problem != null)
                return problem;

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

            return Enqueue(Path.GetFileNameWithoutExtension(file.FileName), rawText, voice, speed, format);
        }

        /// <summary>Queues typed or pasted text for narration into an audio file.</summary>
        [HttpPost("text")]
        [RequestSizeLimit(SpeechController.MaxUploadBytes)]
        public IActionResult FromText([FromBody] TextJobRequest request)
        {
            var problem = RequestValidation.Check(_engine, request.Voice, request.Speed ?? 1.0f);
            if (problem != null)
                return problem;

            var title = string.IsNullOrWhiteSpace(request.Title) ? TitleFromText(request.Text) : request.Title;
            return Enqueue(title, request.Text, request.Voice, request.Speed ?? 1.0f, request.Format ?? AudioFormat.Mp3);
        }

        [HttpPost("{id:guid}/cancel")]
        public IActionResult Cancel(Guid id) => _queue.Cancel(id) ? NoContent() : NotFound();

        /// <summary>Removes a finished job from the list. Its audio file stays in the library.</summary>
        [HttpDelete("{id:guid}")]
        public IActionResult Dismiss(Guid id) => _queue.Dismiss(id) ? NoContent() : NotFound();

        private IActionResult Enqueue(string title, string? text, string? voice, float speed, AudioFormat format)
        {
            try
            {
                var job = _queue.Enqueue(title, text ?? string.Empty, new TtsOptions(voice, speed), format);
                return AcceptedAtAction(nameof(Get), new { id = job.Id }, JobDto.From(job));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static string TitleFromText(string? text)
        {
            var words = (text ?? string.Empty).Split((char[]?)null, 7, StringSplitOptions.RemoveEmptyEntries);
            var title = string.Join(' ', words.Take(6)).TrimEnd('.', ',', ';', ':');
            return title.Length > 0 ? title : "Narration";
        }
    }

    public record TextJobRequest(string Text, string? Title = null, string? Voice = null,
        float? Speed = null, AudioFormat? Format = null);

    public record JobDto(Guid Id, string Title, string State, string? StatusText, double Progress,
        int ChunksDone, int ChunksTotal, double? SecondsRemaining, double? AudioSeconds,
        string Format, string? FileName, string? Error, DateTimeOffset Created)
    {
        public static JobDto From(NarrationJob j) => new(
            j.Id, j.Title, j.State.ToString(), j.StatusText, Math.Round(j.Progress, 4),
            j.ChunksDone, j.ChunksTotal, j.EstimatedRemaining?.TotalSeconds, j.AudioDuration?.TotalSeconds,
            j.Format.ToString(), j.FileName, j.Error, j.Created);
    }
}
