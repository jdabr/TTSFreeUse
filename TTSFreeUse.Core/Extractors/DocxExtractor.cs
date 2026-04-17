using System;
using System.Collections.Generic;
using DocumentFormat.OpenXml.Packaging;

namespace DocToSpeech.Core.Extractors
{
    public class DocxExtractor : ITextExtractor
    {
        public bool CanHandle(string ext) => ext == ".docx";

        public Task<string> ExtractAsync(Stream fileStream)
        {
            using var doc = WordprocessingDocument.Open(fileStream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            var text = body?.InnerText ?? string.Empty;
            return Task.FromResult(text);
        }
    }
}
