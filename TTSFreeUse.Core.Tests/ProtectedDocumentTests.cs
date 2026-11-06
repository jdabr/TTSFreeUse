using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TTSFreeUse.Core.Extractors;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TTSFreeUse.Core.Tests;

/// <summary>
/// TTSFreeUse never bypasses copy protection. These tests pin down which
/// protected files are read and which are refused.
/// The PDFs in TestData were generated from a one-line test document with
/// pypdf, using RC4-128 encryption and the permissions named in each file.
/// </summary>
public class ProtectedDocumentTests
{
    private static Stream TestFile(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Fact]
    public async Task RefusesPasswordProtectedPdf()
    {
        await using var pdf = TestFile("password-required.pdf");
        var ex = await Assert.ThrowsAsync<ProtectedDocumentException>(() => new PdfExtractor().ExtractAsync(pdf));
        Assert.Contains("password-protected", ex.Message);
    }

    [Fact]
    public async Task RefusesPdfWhosePublisherBlocksAccessibilityExtraction()
    {
        await using var pdf = TestFile("protected-no-extraction.pdf");
        var ex = await Assert.ThrowsAsync<ProtectedDocumentException>(() => new PdfExtractor().ExtractAsync(pdf));
        Assert.Contains("accessibility", ex.Message);
    }

    [Theory]
    [InlineData("protected-accessibility-allowed.pdf")] // copying blocked, accessibility tools allowed
    [InlineData("protected-copy-allowed.pdf")]          // encrypted, but no restrictions
    public async Task ReadsPdfWhenPublisherPermitsAccessibilityTools(string file)
    {
        await using var pdf = TestFile(file);
        var text = await new PdfExtractor().ExtractAsync(pdf);
        Assert.Contains("This is a test document for TTSFreeUse.", text);
    }

    [Fact]
    public async Task RefusesEncryptedOrRightsProtectedWordDocument()
    {
        // Encrypted/IRM .docx files are OLE compound files, not zip packages.
        var compound = new byte[512];
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(compound, 0);

        var ex = await Assert.ThrowsAsync<ProtectedDocumentException>(
            () => new DocxExtractor().ExtractAsync(new MemoryStream(compound)));
        Assert.Contains("password-protected or rights-protected", ex.Message);
    }

    [Fact]
    public async Task ReadsDocxFromNonSeekableStream()
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Hello.")))));
        }

        var text = await new DocxExtractor().ExtractAsync(new ForwardOnlyStream(ms.ToArray()));

        Assert.Equal("Hello.", text);
    }

    private class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
