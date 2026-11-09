namespace TTSFreeUse.Core.Extractors
{
    /// <summary>Plain .txt files. Encoding is detected from the BOM, defaulting to UTF-8.</summary>
    public class PlainTextExtractor : ITextExtractor
    {
        public bool CanHandle(string extension) => extension == ".txt";

        public async Task<string> ExtractAsync(Stream fileStream)
        {
            using var reader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync();
        }
    }
}
