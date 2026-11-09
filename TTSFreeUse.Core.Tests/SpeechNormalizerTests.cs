using TTSFreeUse.Core.Pronunciation;
using TTSFreeUse.Core.Transformers;

namespace TTSFreeUse.Core.Tests;

public class SpeechNormalizerTests
{
    // Stands in for the voice's pronunciation dictionary.
    private static readonly HashSet<string> Dictionary = new()
    {
        "warning", "caution", "note", "danger", "grounding", "and", "bonding", "the", "it", "is",
        "important", "led", "psi", "fig", "us", "for", "all", "chapter", "osha",
    };

    private static readonly SpeechNormalizer Normalizer = new(isKnownWord: w => Dictionary.Contains(w));

    [Theory]
    // Acronyms
    [InlineData("Install a GFCI receptacle.", "Install a G F C I receptacle.")]
    [InlineData("Per the NEC, use GFCIs.", "Per the N E C, use G F C I's.")]
    [InlineData("The NEC's rules apply.", "The N E C's rules apply.")]
    [InlineData("WARNING: GROUNDING AND BONDING", "WARNING: GROUNDING AND BONDING")]
    [InlineData("OSHA requires PPE.", "osha requires P P E.")]
    [InlineData("Replace the LED driver.", "Replace the L E D driver.")]
    [InlineData("Use R22 or R410A refrigerant.", "Use R 22 or R 410 A refrigerant.")]
    [InlineData("The HVAC system", "The H vac system")]
    [InlineData("Chapter IV covers Class II locations.", "Chapter 4 covers Class 2 locations.")]
    [InlineData("I said OK.", "I said O K.")]
    // Units
    [InlineData("Rated 125V, 15A.", "Rated 125 volts, 15 amps.")]
    [InlineData("A 1 A fuse", "A 1 amp fuse")]
    [InlineData("Torque lugs to 35 lb-in.", "Torque lugs to 35 pound-inches.")]
    [InlineData("Tighten to 25 ft-lbs and test at 120 psi.", "Tighten to 25 foot-pounds and test at 120 P S I.")]
    [InlineData("Keep it at 72°F.", "Keep it at 72 degrees Fahrenheit.")]
    [InlineData("A 90° bend", "A 90 degree bend")]
    [InlineData("Use a 10 kΩ resistor and 100 µF capacitor.", "Use a 10 kilohm resistor and 100 microfarads capacitor.")]
    [InlineData("Load is 10-20 A.", "Load is 10 to 20 amps.")]
    [InlineData("Cover 200 sq ft at 80%.", "Cover 200 square feet at 80 percent.")]
    [InlineData("Wait 5 min. Then check.", "Wait 5 minutes. Then check.")]
    [InlineData("Five men and 3 m of cable", "Five men and 3 meters of cable")]
    [InlineData("See Class 1 A for details.", "See Class 1 A for details.")]
    // Fractions, inch and foot marks, dimensions
    [InlineData("Use 3/4\" conduit.", "Use 3 quarters inch conduit.")]
    [InlineData("Drill a 1/2 in. hole.", "Drill a one half inch hole.")]
    [InlineData("A 1-1/2\" pipe", "A 1 and a half inch pipe")]
    [InlineData("A 6' ladder", "A 6 foot ladder")]
    [InlineData("Frame with 2x4 studs on a 4x8 ft sheet.", "Frame with 2 by 4 studs on a 4 by 8 feet sheet.")]
    [InlineData("He said \"Chapter 5\" twice.", "He said \"Chapter 5\" twice.")]
    [InlineData("Due 12/25 at 120/240V.", "Due 12/25 at 120/240 volts.")]
    // References and abbreviations
    [InlineData("See NEC §210.8(A)(1).", "See N E C section 210.8 A 1.")]
    [InlineData("See Fig. 3-2 on p. 47.", "See Figure 3-2 on page 47.")]
    [InlineData("Read pp. 4-7 first.", "Read pages 4 to 7 first.")]
    [InlineData("Use copper, e.g., THHN.", "Use copper, for example, T H H N.")]
    [InlineData("Wire, conduit, etc. are listed.", "Wire, conduit, et cetera are listed.")]
    [InlineData("Use No. 12 wire.", "Use number 12 wire.")]
    [InlineData("Opens at 8 AM.", "Opens at 8 A M.")]
    [InlineData("Open Mon.–Fri., 8 AM to 5 PM.", "Open Monday to Friday, 8 A M to 5 P M.")]
    [InlineData("Class I, Division 2 and Part I", "Class 1, Division 2 and Part 1")]
    [InlineData("Use 3/4\" EMT and a 1-1/2\" PVC sleeve.", "Use 3 quarters inch E M T and a 1 and a half inch P V C sleeve.")]
    [InlineData("12 AWG THHN is rated 20 A at 60°C.", "12 A W G T H H N is rated 20 amps at 60 degrees Celsius.")]
    [InlineData("Pages 10–20", "Pages 10 to 20")]
    // Symbols in running text
    [InlineData("Keep ΔT ≤ 5 degrees.", "Keep capital delta T is less than or equal to 5 degrees.")]
    [InlineData("Area = πr²", "Area equals pi r squared")]
    [InlineData("Tolerance ± 2 mm", "Tolerance plus or minus 2 millimeters")]
    [InlineData("H₂O", "H 2 O")]
    // Ordinary prose is left alone
    [InlineData("It is important for all of us.", "It is important for all of us.")]
    [InlineData("I have 3 apples and 2 in my bag.", "I have 3 apples and 2 in my bag.")]
    public void SpeaksTechnicalText(string written, string spoken) =>
        Assert.Equal(spoken, Normalizer.Normalize(written));

    [Fact]
    public void WithoutADictionaryShortCapitalsAreSpelledAndHeadingsKept()
    {
        var normalizer = new SpeechNormalizer();
        Assert.Equal("N E C NOTE: Use the G F C I.", normalizer.Normalize("NEC NOTE: Use the GFCI."));
        Assert.Equal("GROUNDING", normalizer.Normalize("GROUNDING"));
    }
}

public class PronunciationStoreTests
{
    [Fact]
    public void AppliesWholeWordsAndSavesToDisk()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "p.json");
        var store = new PronunciationStore(path);
        store.Replace(new[]
        {
            new PronunciationEntry("Romex", "roh mex"),
            new PronunciationEntry("lb-in", "inch pounds"),
            new PronunciationEntry("AC", "alternating current", MatchCase: true),
        });

        Assert.Equal("Run roh mex, not romexes. 5 inch pounds. alternating current but not ac or ACT.",
            store.Apply("Run ROMEX, not romexes. 5 lb-in. AC but not ac or ACT."));

        // A new store reads the saved file.
        Assert.Equal(3, new PronunciationStore(path).Entries.Count);
    }

    [Fact]
    public void UserListWinsOverBuiltInRules()
    {
        using var dir = new TempDir();
        var store = new PronunciationStore(Path.Combine(dir.Path, "p.json"));
        store.Replace(new[] { new PronunciationEntry("NEC", "National Electrical Code") });
        var normalizer = new SpeechNormalizer(store);

        Assert.Equal("The National Electrical Code and G F C I", normalizer.Normalize("The NEC and GFCI"));
    }

    [Fact]
    public void RejectsDuplicatesAndSkipsBlankRows()
    {
        using var dir = new TempDir();
        var store = new PronunciationStore(Path.Combine(dir.Path, "p.json"));

        var ex = Assert.Throws<ArgumentException>(() => store.Replace(new[]
        {
            new PronunciationEntry("NEC", "a"), new PronunciationEntry("nec", "b"),
        }));
        Assert.Contains("twice", ex.Message);

        store.Replace(new[] { new PronunciationEntry("  ", "x"), new PronunciationEntry(" NEC ", " N E C ") });
        Assert.Equal(new PronunciationEntry("NEC", "N E C"), Assert.Single(store.Entries));
    }

    [Fact]
    public void CorruptFileStartsEmptyWithAnError()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "p.json");
        File.WriteAllText(path, "{ not json");

        var store = new PronunciationStore(path);

        Assert.Empty(store.Entries);
        Assert.NotNull(store.LoadError);
    }
}
