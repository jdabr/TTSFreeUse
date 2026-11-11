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
                return await OnLargeStack(() => extractor.ExtractAsync(stream));
            }
            catch (DocumentReadException)
            {
                throw; // already explained for the user
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

        // Measured: a PDF whose page list is nested ~5,000 levels deep overflows
        // the normal 1 MB stack inside the PDF library; with 256 MB, a million
        // levels parse (using ~2 GB of memory before the stack is the limit).
        private const int ParserStackBytes = 256 * 1024 * 1024;

        /// <summary>
        /// Runs a document parser on its own thread with a large stack. Parsers
        /// work through nested structures recursively, and a damaged or
        /// malicious file can nest deeply enough to overflow a normal stack. A
        /// stack overflow can't be caught: it would close TTSFreeUse, losing
        /// any conversions in progress. The stack is reserved address space;
        /// memory is only used as it's needed.
        /// </summary>
        private static Task<string> OnLargeStack(Func<Task<string>> parse)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { result.SetResult(parse().GetAwaiter().GetResult()); }
                catch (Exception ex) { result.SetException(ex); }
            }, ParserStackBytes)
            {
                IsBackground = true,
                Name = "TTSFreeUse document reader",
            };
            thread.Start();
            return result.Task;
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
