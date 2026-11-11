using Microsoft.AspNetCore.Mvc;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Jobs;

namespace TTSFreeUse.Api.Controllers
{
    /// <summary>Finished audio files, saved in the library folder on this computer.</summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LibraryController : ControllerBase
    {
        private readonly AudioLibrary _library;

        public LibraryController(AudioLibrary library)
        {
            _library = library;
        }

        [HttpGet]
        public IActionResult List() => Ok(new { directory = _library.Directory, items = _library.List() });

        /// <summary>Plays or downloads a file. Supports seeking (HTTP range requests).</summary>
        [HttpGet("{fileName}")]
        public IActionResult Get(string fileName, [FromQuery] bool download = false)
        {
            var path = _library.Resolve(fileName);
            if (path == null)
                return NotFound();

            AudioFormats.TryFromExtension(Path.GetExtension(path), out var format);
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920, FileOptions.Asynchronous);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return NotFound(); // deleted a moment ago
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Conflict("This file can't be opened right now. Another program may be using it.");
            }
            return download
                ? File(stream, format.ContentType(), Path.GetFileName(path), enableRangeProcessing: true)
                : File(stream, format.ContentType(), enableRangeProcessing: true);
        }

        [HttpDelete("{fileName}")]
        public IActionResult Delete(string fileName)
        {
            try
            {
                return _library.Delete(fileName) ? NoContent() : NotFound();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Conflict("This file couldn't be deleted. If it's open in another program " +
                                "(such as a music player), close it and try again.");
            }
        }
    }
}
