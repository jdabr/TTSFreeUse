using TTSFreeUse.Core.Transformers;

namespace TTSFreeUse.Core.Tests;

public class TextChunkerTests
{
    private static string[] Words(IEnumerable<string> chunks) =>
        string.Join(" ", chunks).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [InlineData("See Fig. 3-2 for the wiring.")]
    [InlineData("It is on p. 47 of the guide.")]
    [InlineData("Use copper, e.g. THHN wire.")]
    [InlineData("Ask Dr. Smith or J. Jones.")]
    [InlineData("Drill a 1/2 in. hole here.")]
    public void AbbreviationsDontEndASentence(string sentence)
    {
        // Small enough that every real sentence becomes its own chunk.
        var chunks = new TextChunker { MaxChunkLength = 30 }.Chunk("First sentence here. " + sentence);
        Assert.Equal(new[] { "First sentence here.", sentence }, chunks);
    }

    [Fact]
    public void PreservesSentencePunctuation()
    {
        var chunks = new TextChunker { MaxChunkLength = 20 }
            .Chunk("Is this a question? Yes! It is. \"Quoted end.\" Done… Next.");

        var joined = string.Join(" ", chunks);
        Assert.Equal("Is this a question? Yes! It is. \"Quoted end.\" Done… Next.", joined);
    }

    [Fact]
    public void ShortTextIsOneChunk()
    {
        Assert.Equal(new[] { "One. Two. Three." }, new TextChunker().Chunk("One. Two. Three."));
    }

    [Fact]
    public void NoChunkExceedsTheLimit()
    {
        var text = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"Sentence number {i} is here."));
        var chunks = new TextChunker { MaxChunkLength = 100 }.Chunk(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 100));
        Assert.Equal(Words(new[] { text }), Words(chunks)); // nothing lost or reordered
    }

    [Fact]
    public void LongSentenceSplitsAtClauseBoundaries()
    {
        var text = "This clause is fairly long, and this one is long too; finally this is the end.";
        var chunks = new TextChunker { MaxChunkLength = 30 }.Chunk(text);

        Assert.Equal(new[] { "This clause is fairly long,", "and this one is long too;", "finally this is the end." }, chunks);
    }

    [Fact]
    public void UnbreakableTokenIsHardSplitAtTheLimit()
    {
        var url = "http://" + new string('x', 150);
        var chunks = new TextChunker { MaxChunkLength = 60 }.Chunk(url);

        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 60));
        Assert.Equal(url, string.Concat(chunks));
    }

    [Fact]
    public void ShortParagraphsArePackedTogether()
    {
        var chunks = new TextChunker { MaxChunkLength = 100 }.Chunk("Para one.\n\nPara two.\n\nPara three.");
        Assert.Equal(new[] { "Para one. Para two. Para three." }, chunks);
    }

    [Fact]
    public void LongRunsOfAbbreviationsStayFast()
    {
        // "A. B. C." never ends a sentence, so the whole run is one sentence
        // that has to be split; this used to take time proportional to the
        // square of its length (about 40 seconds for a megabyte).
        var text = string.Concat(Enumerable.Repeat("A. ", 300_000));

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var chunks = new TextChunker().Chunk(text);

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Chunking took {timer.Elapsed}.");
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 800));
        Assert.Equal(300_000, Words(chunks).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n\n\n")]
    public void EmptyInputGivesNoChunks(string input)
    {
        Assert.Empty(new TextChunker().Chunk(input));
    }
}
