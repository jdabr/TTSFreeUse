using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

namespace TTSFreeUse.Core.Extractors
{
    public class DocxExtractor : ITextExtractor
    {
        public bool CanHandle(string ext) => ext == ".docx";

        // Password-encrypted and rights-managed (IRM) Word files are stored in an
        // OLE compound file container instead of a normal .docx zip.
        private static readonly byte[] CompoundFileSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        public Task<string> ExtractAsync(Stream fileStream)
        {
            if (!fileStream.CanSeek)
            {
                var copy = new MemoryStream();
                fileStream.CopyTo(copy);
                copy.Position = 0;
                fileStream = copy;
            }

            var header = new byte[CompoundFileSignature.Length];
            var read = fileStream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            fileStream.Position = 0;
            if (read == header.Length && header.AsSpan().SequenceEqual(CompoundFileSignature))
                throw new ProtectedDocumentException(
                    "This Word document is password-protected or rights-protected. TTSFreeUse can only " +
                    "read documents that open without a password.");

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
            AppendText(paragraph, sb);
            return sb.ToString();
        }

        private static void AppendText(OpenXmlElement element, StringBuilder sb)
        {
            foreach (var child in element.ChildElements)
            {
                switch (child)
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
                    // Equations are stored as math markup, not text; read them
                    // as words ("x squared plus …") instead of dropping them.
                    case M.OfficeMath math:
                        sb.Append(' ').Append(OmmlSpeech.Speak(math)).Append(' ');
                        break;
                    default:
                        AppendText(child, sb);
                        break;
                }
            }
        }
    }
}
