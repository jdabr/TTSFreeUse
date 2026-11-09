namespace TTSFreeUse.Core.Extractors
{
    /// <summary>Reads the text out of one kind of document.</summary>
    public interface ITextExtractor
    {
        /// <param name="extension">Lowercase file extension including the dot, e.g. ".pdf".</param>
        bool CanHandle(string extension);

        /// <summary>
        /// Returns the document's text with paragraphs separated by a blank
        /// line. Throws <see cref="ProtectedDocumentException"/> for files that
        /// are password-, DRM-, or permission-protected.
        /// </summary>
        Task<string> ExtractAsync(Stream fileStream);
    }
}
