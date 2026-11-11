using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Transformers;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TTSFreeUse.Core.Tests;

public class DocxExtractorTests
{
    private static MemoryStream BuildDocx(params OpenXmlElement[] bodyElements)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(bodyElements));
        }
        ms.Position = 0;
        return ms;
    }

    private static W.Paragraph Para(params OpenXmlElement[] runContent) => new(new W.Run(runContent));

    /// <summary>Builds a .docx whose body is the given WordprocessingML, as Word itself would write it.</summary>
    internal static MemoryStream BuildDocxXml(string bodyXml)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var xml = "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                      "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" " +
                      "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
                      "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
                      "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                      "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                      "xmlns:v=\"urn:schemas-microsoft-com:vml\" mc:Ignorable=\"wps\">" +
                      $"<w:body>{bodyXml}</w:body></w:document>";
            doc.AddMainDocumentPart().FeedData(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task TextBoxesAreReadOnceAfterTheirParagraph()
    {
        // Word saves every text box twice: a current copy (mc:Choice) and a
        // fallback copy for old versions of Word (mc:Fallback).
        const string box = "<w:txbxContent><w:p><w:r><w:t>Inside the box.</w:t></w:r></w:p></w:txbxContent>";
        using var docx = BuildDocxXml(
            "<w:p><w:r><w:t xml:space=\"preserve\">Before. </w:t></w:r><w:r><mc:AlternateContent>" +
            $"<mc:Choice Requires=\"wps\"><w:drawing><wp:anchor><a:graphic><a:graphicData><wps:wsp><wps:txbx>{box}</wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>" +
            $"<mc:Fallback><w:pict><v:shape><v:textbox>{box}</v:textbox></v:shape></w:pict></mc:Fallback>" +
            "</mc:AlternateContent></w:r><w:r><w:t>After.</w:t></w:r></w:p>");

        Assert.Equal("Before. After.\n\nInside the box.", await new DocxExtractor().ExtractAsync(docx));
    }

    [Fact]
    public async Task TrackedMovesAreReadOnlyWhereTheTextWentTo()
    {
        // With tracked changes on, moved text is stored at both its old and new place.
        using var docx = BuildDocx(new W.Paragraph(
            new W.MoveFromRun(new W.Run(new W.Text("Moved words. "))),
            new W.Run(new W.Text("Other words. ")),
            new W.MoveToRun(new W.Run(new W.Text("Moved words.")))));

        Assert.Equal("Other words. Moved words.", await new DocxExtractor().ExtractAsync(docx));
    }

    [Fact]
    public async Task SeparatesParagraphs()
    {
        using var docx = BuildDocx(Para(new W.Text("End of one.")), Para(new W.Text("Next")));
        var text = await new DocxExtractor().ExtractAsync(docx);
        Assert.Equal("End of one.\n\nNext", text);
    }

    [Fact]
    public async Task TabsAndLineBreaksBecomeSpaces()
    {
        using var docx = BuildDocx(Para(
            new W.Text("Column"), new W.TabChar(), new W.Text("Tabbed"), new W.Break(), new W.Text("After")));
        Assert.Equal("Column Tabbed After", await new DocxExtractor().ExtractAsync(docx));
    }

    [Fact]
    public async Task TableCellsAreSeparated()
    {
        using var docx = BuildDocx(new W.Table(new W.TableRow(
            new W.TableCell(Para(new W.Text("Cell A"))),
            new W.TableCell(Para(new W.Text("Cell B"))))));
        Assert.Equal("Cell A\n\nCell B", await new DocxExtractor().ExtractAsync(docx));
    }

    [Fact]
    public async Task EmptyDocumentGivesEmptyText()
    {
        using var docx = BuildDocx();
        Assert.Equal(string.Empty, await new DocxExtractor().ExtractAsync(docx));
    }
}

public class PdfExtractorTests
{
    /// <summary>Builds a PDF whose pages have a running header and a page number footer.</summary>
    private static MemoryStream BuildBook(string[][] pageLines, Func<int, string>? pageNumber = null)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int p = 0; p < pageLines.Length; p++)
        {
            var page = builder.AddPage(612, 792);
            page.AddText($"My Technical Book - Chapter {p + 1}", 10, new PdfPoint(72, 750), font);
            double y = 700;
            foreach (var line in pageLines[p])
            {
                page.AddText(line, 12, new PdfPoint(72, y), font);
                y -= 16;
            }
            page.AddText((pageNumber ?? (i => (i + 1).ToString()))(p), 10, new PdfPoint(300, 40), font);
        }
        return new MemoryStream(builder.Build());
    }

    private static Task<string> Extract(Stream pdf) => new PdfExtractor().ExtractAsync(pdf);

    [Fact]
    public async Task RemovesRunningHeadersAndPageNumbers()
    {
        using var pdf = BuildBook(new[]
        {
            new[] { "Page one body." }, new[] { "Page two body." },
            new[] { "Page three body." }, new[] { "Page four body." },
        });

        var text = await Extract(pdf);

        Assert.Equal("Page one body.\nPage two body.\nPage three body.\nPage four body.", text);
    }

    [Fact]
    public async Task RemovesRomanNumeralPageNumbersButKeepsLookalikeWords()
    {
        string[] roman = { "i", "ii", "iii", "iv" };
        using var pdf = BuildBook(new[]
        {
            new[] { "Preface text." }, new[] { "civil" }, new[] { "mix" }, new[] { "More preface." },
        }, p => roman[p]);

        var text = await Extract(pdf);

        Assert.Equal("Preface text.\ncivil\nmix\nMore preface.", text);
    }

    [Fact]
    public async Task KeepsLineBreaksSoTheCleanerCanRejoinHyphenatedWords()
    {
        using var pdf = BuildBook(new[]
        {
            new[] { "The compu-", "ter works." }, new[] { "Two." }, new[] { "Three." },
        });

        var cleaned = new TextCleaner().Clean(await Extract(pdf));

        Assert.Equal("The computer works. Two. Three.", cleaned);
    }

    [Fact]
    public async Task ShortDocumentsKeepRepeatedLines()
    {
        // With fewer than 3 pages there isn't enough evidence that a repeated
        // line is a header, so the chapter title lines are kept.
        using var pdf = BuildBook(new[] { new[] { "Body one." }, new[] { "Body two." } });

        var text = await Extract(pdf);

        Assert.Contains("My Technical Book - Chapter 1", text);
        Assert.Contains("Body two.", text);
        Assert.DoesNotContain("\n1\n", "\n" + text + "\n"); // page numbers still removed
    }

    [Fact]
    public async Task PdfWithoutTextGivesNoChunks()
    {
        // e.g. a scanned book: pages exist but have no text layer.
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);
        using var pdf = new MemoryStream(builder.Build());

        var chunks = new TextChunker().Chunk(new TextCleaner().Clean(await Extract(pdf)));

        Assert.Empty(chunks);
    }
}

public class PlainTextExtractorTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public async Task DetectsEncodingFromByteOrderMark(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Café — naïve")).ToArray();

        var text = await new PlainTextExtractor().ExtractAsync(new MemoryStream(bytes));

        Assert.Equal("Café — naïve", text);
    }

    [Fact]
    public async Task DefaultsToUtf8WithoutByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetBytes("Café");
        Assert.Equal("Café", await new PlainTextExtractor().ExtractAsync(new MemoryStream(bytes)));
    }

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".pdf", false)]
    public void HandlesOnlyTxt(string ext, bool expected)
    {
        Assert.Equal(expected, new PlainTextExtractor().CanHandle(ext));
    }
}
