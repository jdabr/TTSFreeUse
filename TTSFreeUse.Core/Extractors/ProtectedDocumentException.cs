namespace TTSFreeUse.Core.Extractors
{
    /// <summary>
    /// Thrown when a document is password-protected, DRM-protected, or its
    /// publisher doesn't permit text extraction for accessibility tools.
    /// TTSFreeUse never bypasses these protections. The message is written for
    /// the end user and is safe to show as-is.
    /// </summary>
    public class ProtectedDocumentException : Exception
    {
        public ProtectedDocumentException(string message) : base(message) { }
    }
}
