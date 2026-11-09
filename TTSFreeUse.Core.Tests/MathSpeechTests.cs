using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Transformers;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TTSFreeUse.Core.Tests;

public class MathSymbolsTests
{
    [Theory]
    [InlineData("x=2", "x equals 2")]
    [InlineData("a≤b", "a is less than or equal to b")]
    [InlineData("Δt", "capital delta t")]
    [InlineData("ωt+φ", "omega t plus phi")]
    [InlineData("2πr", "2 pi r")]
    [InlineData("3.14", "3.14")]
    [InlineData("1,000", "1,000")]
    [InlineData("f(x,y)", "f x, y")]
    [InlineData("xy", "x y")]
    [InlineData("sin", "sine")]
    [InlineData("speed", "speed")]
    [InlineData("x∈ℝ", "x is in the real numbers")]
    public void SpeaksMathText(string math, string spoken) =>
        Assert.Equal(spoken, MathSymbols.Speak(math));
}

public class OmmlSpeechTests
{
    // Builders for Word equation markup.
    private static M.Run R(string text) => new(new M.Text(text));
    private static M.Base B(params OpenXmlElement[] content) => new(content);
    private static M.OfficeMath Math(params OpenXmlElement[] content) => new(content);

    private static M.Superscript Sup(string b, string sup) =>
        new(B(R(b)), new M.SuperArgument(R(sup)));

    private static M.Nary Nary(string symbol, string from, string to, params OpenXmlElement[] body) =>
        new(new M.NaryProperties(new M.AccentChar { Val = symbol }),
            new M.SubArgument(R(from)), new M.SuperArgument(R(to)), B(body));

    [Fact]
    public void Pythagoras() =>
        Assert.Equal("x squared plus y squared equals z squared",
            OmmlSpeech.Speak(Math(Sup("x", "2"), R("+"), Sup("y", "2"), R("="), Sup("z", "2"))));

    [Fact]
    public void SimpleFraction() =>
        Assert.Equal("a over b",
            OmmlSpeech.Speak(Math(new M.Fraction(new M.Numerator(R("a")), new M.Denominator(R("b"))))));

    [Fact]
    public void ComplexFractionMarksWhereItEnds() =>
        Assert.Equal("the fraction a plus b, over c minus d, equals 1",
            OmmlSpeech.Speak(Math(
                new M.Fraction(new M.Numerator(R("a+b")), new M.Denominator(R("c−d"))), R("=1"))));

    [Fact]
    public void SquareRootAndCubeRoot()
    {
        Assert.Equal("the square root of x",
            OmmlSpeech.Speak(Math(new M.Radical(new M.Degree(), B(R("x"))))));
        Assert.Equal("the cube root of 27",
            OmmlSpeech.Speak(Math(new M.Radical(new M.Degree(R("3")), B(R("27"))))));
    }

    [Fact]
    public void SumWithLimits() =>
        Assert.Equal("the sum from i equals 1 to n of i squared",
            OmmlSpeech.Speak(Math(Nary("∑", "i=1", "n", Sup("i", "2")))));

    [Fact]
    public void IntegralIsTheDefaultNary() =>
        Assert.Equal("the integral from 0 to 1 of x d x",
            OmmlSpeech.Speak(Math(new M.Nary(
                new M.SubArgument(R("0")), new M.SuperArgument(R("1")), B(R("x dx"))))));

    [Fact]
    public void PowerWithLongExponentPauses() =>
        Assert.Equal("e to the power of i pi, plus 1 equals 0",
            OmmlSpeech.Speak(Math(new M.Superscript(B(R("e")), new M.SuperArgument(R("iπ"))), R("+1=0"))));

    [Fact]
    public void AbsoluteValue() =>
        Assert.Equal("the absolute value of x",
            OmmlSpeech.Speak(Math(new M.Delimiter(
                new M.DelimiterProperties(new M.BeginChar { Val = "|" }, new M.EndChar { Val = "|" }),
                B(R("x"))))));

    [Fact]
    public void Subscript() =>
        Assert.Equal("V sub 1 equals I R sub 1",
            OmmlSpeech.Speak(Math(
                new M.Subscript(B(R("V")), new M.SubArgument(R("1"))), R("=I"),
                new M.Subscript(B(R("R")), new M.SubArgument(R("1"))))));

    [Fact]
    public void FunctionAndLimit()
    {
        Assert.Equal("sine of x",
            OmmlSpeech.Speak(Math(new M.MathFunction(new M.FunctionName(R("sin")), B(R("x"))))));

        var limit = new M.MathFunction(
            new M.FunctionName(new M.LimitLower(B(R("lim")), new M.Limit(R("x→0")))),
            B(R("f"), new M.Delimiter(B(R("x")))));
        Assert.Equal("the limit as x approaches 0 of f x", OmmlSpeech.Speak(Math(limit)));
    }

    [Fact]
    public async Task WordEquationsAreReadInsteadOfDropped()
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                new W.Paragraph(
                    new W.Run(new W.Text("The area is") { Space = SpaceProcessingModeValues.Preserve }),
                    Math(R("A=π"), Sup("r", "2")),
                    new W.Run(new W.Text("for any circle."))),
                // A display equation on its own line.
                new W.Paragraph(new M.Paragraph(Math(R("F=ma"))))));
        }
        ms.Position = 0;

        var text = new TextCleaner().Clean(await new DocxExtractor().ExtractAsync(ms));

        Assert.Equal("The area is A equals pi r squared for any circle.\n\nF equals m a", text);
    }
}
