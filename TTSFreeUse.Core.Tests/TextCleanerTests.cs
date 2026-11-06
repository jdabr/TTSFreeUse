using TTSFreeUse.Core.Transformers;

namespace TTSFreeUse.Core.Tests;

public class TextCleanerTests
{
    private readonly TextCleaner _cleaner = new();

    [Fact]
    public void KeepsAccentsDashesAndMathSymbols()
    {
        Assert.Equal("Café naïve — x ≤ 5 × 2 ±1", _cleaner.Clean("Café naïve — x ≤ 5 × 2 ±1"));
    }

    [Fact]
    public void StraightensSmartQuotes()
    {
        Assert.Equal("\"Hi,\" she said. 'Yes.'", _cleaner.Clean("“Hi,” she said. ‘Yes.’"));
    }

    [Fact]
    public void ExpandsLigatures()
    {
        Assert.Equal("the file flows offline", _cleaner.Clean("the ﬁle ﬂows oﬀline"));
    }

    [Fact]
    public void RemovesInvisibleCharacters()
    {
        Assert.Equal("hyphenation", _cleaner.Clean("hy­phen​ation﻿"));
    }

    [Fact]
    public void RejoinsWordsHyphenatedAcrossLines()
    {
        Assert.Equal("the computer starts", _cleaner.Clean("the compu-\nter starts"));
    }

    [Fact]
    public void KeepsHyphenWhenNextLineIsNotLowercase()
    {
        Assert.Equal("COVID-19 and Wi-Fi", _cleaner.Clean("COVID-\n19 and Wi-\nFi"));
    }

    [Fact]
    public void BlankLinesBecomeParagraphBreaksAndSingleNewlinesBecomeSpaces()
    {
        var input = "First line\nwraps here.\r\n\r\n\n  Second   paragraph.\n\nThird.";
        Assert.Equal("First line wraps here.\n\nSecond paragraph.\n\nThird.", _cleaner.Clean(input));
    }

    [Fact]
    public void ReplacesControlCharactersWithSpaces()
    {
        Assert.Equal("a b", _cleaner.Clean("a\u0007b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n \t ")]
    public void EmptyOrWhitespaceInputGivesEmptyString(string input)
    {
        Assert.Equal(string.Empty, _cleaner.Clean(input));
    }
}
