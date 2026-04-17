using System;
using System.Collections.Generic;
using UglyToad.PdfPig;

namespace DocToSpeech.Core.Extractors
{
    public class PdfExtractor : ITextExtractor
    {
        public bool CanHandle(string ext) => ext == ".pdf";

        public Task<string> ExtractAsync(Stream fileStream)
        {
            using var doc = PdfDocument.Open(fileStream);
            var text = string.Join(" ", doc.GetPages()
                .SelectMany(p => p.GetWords())
                .Select(w => w.Text));
            return Task.FromResult(text);
        }
    }
}
