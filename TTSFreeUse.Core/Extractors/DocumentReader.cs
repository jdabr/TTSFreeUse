namespace TTSFreeUse.Core.Extractors
{
    /// <summary>
    /// Picks the right extractor for a file and turns every failure into a
    /// <see cref="DocumentReadException"/> with a message that can be shown to
    /// the user as-is.
    /// </summary>
    public class DocumentReader
    {
        private readonly IEnumerable<ITextExtractor> _extractors;

        public DocumentReader(IEnumerable<ITextExtractor> extractors)
        {
            _extractors = extractors;
        }

        public const string SupportedTypes = ".pdf, .docx, .txt";

        public async Task<string> ReadAsync(Stream stream, string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var extractor = Find(fileName)
                ?? throw new DocumentReadException($"Unsupported file type: {ext}. Supported: {SupportedTypes}");

            try
            {
                return await extractor.ExtractAsync(stream);
            }
            catch (ProtectedDocumentException ex)
            {
                throw new DocumentReadException(ex.Message, ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Corrupt or truncated files land here.
                throw new DocumentReadException(
                    $"Couldn't read this {ext} file. It may be damaged or password-protected.", ex);
            }
        }

        private ITextExtractor? Find(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return _extractors.FirstOrDefault(e => e.CanHandle(ext));
        }
    }

    /// <summary>A document couldn't be read. The message is written for end users.</summary>
    public class DocumentReadException : Exception
    {
        public DocumentReadException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
