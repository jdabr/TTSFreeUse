using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace TTSFreeUse.Core.Extractors
{
    public class DocxExtractor : ITextExtractor
    {
        public bool CanHandle(string ext) => ext == ".docx";

        public Task<string> ExtractAsync(Stream fileStream)
        {
            using var doc = WordprocessingDocument.Open(fileStream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return Task.FromResult(string.Empty);

            // Body.InnerText glues paragraphs together with no separator
            // ("end of one" + "Next" -> "end of oneNext"), so walk paragraphs
            // explicitly and separate them with a blank line.
            var paragraphs = body.Descendants<Paragraph>()
                .Select(ParagraphText)
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return Task.FromResult(string.Join("\n\n", paragraphs));
        }

        private static string ParagraphText(Paragraph paragraph)
        {
            var sb = new StringBuilder();
            foreach (var element in paragraph.Descendants())
            {
                switch (element)
                {
                    case Text t:
                        sb.Append(t.Text);
                        break;
                    // Tabs and manual line breaks have no InnerText; without
                    // this the words on either side run together.
                    case TabChar:
                    case Break:
                    case CarriageReturn:
                        sb.Append(' ');
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
