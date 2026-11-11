using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;

namespace TTSFreeUse.Core.Extractors
{
    public class DocxExtractor : ITextExtractor
    {
        public bool CanHandle(string extension) => extension == ".docx";

        /// <summary>
        /// Deepest nesting of document markup that is read. Real documents stay
        /// far below this (tables in text boxes in tables reach a few dozen
        /// levels); a file nested deeper is damaged or made to crash readers.
        /// </summary>
        internal const int MaxMarkupDepth = 1000;

        /// <summary>
        /// Most characters of markup read from the document: tens of thousands
        /// of pages. Stops a small file that unzips to gigabytes.
        /// </summary>
        internal const long MaxMarkupCharacters = 100_000_000;

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

            using var doc = WordprocessingDocument.Open(fileStream, false,
                new OpenSettings { MaxCharactersInPart = MaxMarkupCharacters });
            var main = doc.MainDocumentPart;
            if (main == null)
                return Task.FromResult(string.Empty);

            // Checked with a streaming reader before the document is loaded,
            // because loading it recurses once per level of nesting.
            CheckNesting(main);

            var body = main.Document?.Body;
            if (body == null)
                return Task.FromResult(string.Empty);

            var paragraphs = new List<string>();
            AddParagraphs(body, paragraphs);
            return Task.FromResult(string.Join("\n\n", paragraphs));
        }

        private static void CheckNesting(OpenXmlPart part)
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaxMarkupCharacters,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth > MaxMarkupDepth)
                    throw new DocumentReadException(
                        "This Word document is nested too deeply to read. It may be damaged: try opening " +
                        "it in Word and saving a new copy.");
            }
        }

        /// <summary>
        /// Adds the text of each paragraph in <paramref name="container"/> (the
        /// body, a table, a content control…) as a separate entry. Body.InnerText
        /// would glue paragraphs together ("end of one" + "Next" → "end of oneNext").
        /// </summary>
        private static void AddParagraphs(OpenXmlElement container, List<string> paragraphs)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            foreach (var child in container.ChildElements)
            {
                switch (child)
                {
                    case Paragraph paragraph:
                        AddParagraph(paragraph, paragraphs);
                        break;
                    case AlternateContent alternatives:
                        if (OneAlternative(alternatives) is { } chosen)
                            AddParagraphs(chosen, paragraphs);
                        break;
                    default:
                        AddParagraphs(child, paragraphs);
                        break;
                }
            }
        }

        private static void AddParagraph(Paragraph paragraph, List<string> paragraphs)
        {
            var text = new StringBuilder();
            var textBoxParagraphs = new List<Paragraph>();
            AppendText(paragraph, text, textBoxParagraphs);
            if (!string.IsNullOrWhiteSpace(text.ToString()))
                paragraphs.Add(text.ToString());

            // A text box is read after the paragraph it's placed in, so it
            // doesn't interrupt that paragraph's sentences.
            foreach (var inner in textBoxParagraphs)
                AddParagraph(inner, paragraphs);
        }

        private static void AppendText(OpenXmlElement element, StringBuilder text, List<Paragraph> textBoxParagraphs)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            foreach (var child in element.ChildElements)
            {
                switch (child)
                {
                    case Text t:
                        text.Append(t.Text);
                        break;
                    // Tabs and manual line breaks have no InnerText; without
                    // this the words on either side run together.
                    case TabChar:
                    case Break:
                    case CarriageReturn:
                        text.Append(' ');
                        break;
                    // Equations are stored as math markup, not text; read them
                    // as words ("x squared plus …") instead of dropping them.
                    case M.OfficeMath math:
                        text.Append(' ').Append(OmmlSpeech.Speak(math)).Append(' ');
                        break;
                    // A paragraph inside a paragraph is in a text box.
                    case Paragraph inner:
                        textBoxParagraphs.Add(inner);
                        break;
                    // Word saves shapes and text boxes twice: a current version and
                    // a fallback for old versions of Word. Read only one.
                    case AlternateContent alternatives:
                        if (OneAlternative(alternatives) is { } chosen)
                            AppendText(chosen, text, textBoxParagraphs);
                        break;
                    // With tracked changes on, moved text is also kept at its old
                    // place; it's read where it was moved to.
                    case MoveFromRun:
                        break;
                    default:
                        AppendText(child, text, textBoxParagraphs);
                        break;
                }
            }
        }

        private static OpenXmlElement? OneAlternative(AlternateContent alternatives) =>
            (OpenXmlElement?)alternatives.GetFirstChild<AlternateContentChoice>()
            ?? alternatives.GetFirstChild<AlternateContentFallback>();
    }
}
