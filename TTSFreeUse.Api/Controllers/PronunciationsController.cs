using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.Pronunciation;
using TTSFreeUse.Core.Transformers;

namespace TTSFreeUse.Api.Controllers
{
    /// <summary>
    /// The user's pronunciation list ("say NEC as N E C"), and a preview of
    /// exactly what the voice will be given for some text.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PronunciationsController : ControllerBase
    {
        private readonly PronunciationStore _store;
        private readonly NarrationPipeline _pipeline;
        private readonly TextCleaner _cleaner;

        public PronunciationsController(PronunciationStore store, NarrationPipeline pipeline, TextCleaner cleaner)
        {
            _store = store;
            _pipeline = pipeline;
            _cleaner = cleaner;
        }

        /// <summary>The saved list, and where it is stored.</summary>
        [HttpGet]
        public IActionResult Get() =>
            Ok(new { file = _store.FilePath, error = _store.LoadError, entries = _store.Entries });

        /// <summary>Replaces the whole list.</summary>
        [HttpPut]
        public IActionResult Put([FromBody] List<PronunciationEntry> entries)
        {
            try
            {
                _store.Replace(entries);
                return Ok(new { file = _store.FilePath, entries = _store.Entries });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (IOException ex)
            {
                return Problem($"Couldn't save the pronunciation list: {ex.Message}");
            }
        }

        /// <summary>Shows the text exactly as the voice will read it.</summary>
        [HttpPost("preview")]
        public IActionResult Preview([FromBody] PreviewRequest request)
        {
            var text = request.Text ?? "";
            if (text.Length > 20_000)
                return BadRequest("Preview up to 20,000 characters at a time.");
            return Ok(new { spoken = _pipeline.ToSpoken(_cleaner.Clean(text)) });
        }
    }

    public record PreviewRequest(string? Text);
}
