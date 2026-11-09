using System.Text;
using System.Text.RegularExpressions;
using TTSFreeUse.Core.Pronunciation;

namespace TTSFreeUse.Core.Transformers
{
    /// <summary>
    /// Rewrites technical text into the words a listener expects to hear,
    /// just before it is sent to the voice: "15A" → "15 amps", "GFCI" →
    /// "G F C I", "Fig. 3" → "Figure 3", "§210.8(A)" → "section 210.8 A".
    /// The user's own pronunciation list is applied first, so it always wins.
    /// Displayed text is never changed — only what the voice is given.
    /// </summary>
    public class SpeechNormalizer
    {
        private readonly PronunciationStore? _pronunciations;
        private readonly Func<string, bool?> _isKnownWord;

        /// <param name="pronunciations">The user's pronunciation list, if any.</param>
        /// <param name="isKnownWord">
        /// Whether the voice knows a (lowercase) word, e.g. from its
        /// pronunciation dictionary; null means "can't tell". Used to decide
        /// whether a capitalized word like "WARNING" is a word or an acronym.
        /// </param>
        public SpeechNormalizer(PronunciationStore? pronunciations = null, Func<string, bool?>? isKnownWord = null)
        {
            _pronunciations = pronunciations;
            _isKnownWord = isKnownWord ?? (_ => null);
        }

        public string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (_pronunciations != null)
                text = _pronunciations.Apply(text);

            text = SectionSigns(text);
            text = Abbreviations(text);
            text = Dimensions.Replace(text, " by ");
            text = InchAndFootMarks(text);
            text = Units(text);
            // After units, so the "A" in "8 AM" isn't taken for amps.
            text = MorningTime.Replace(text, " A M");
            text = EveningTime.Replace(text, " P M");
            text = SectionReferences(text);
            text = Fractions(text);
            text = Ranges(text);
            text = Acronyms(text);
            text = Symbols(text);
            return MultipleSpaces.Replace(text, " ").Trim();
        }

        private static readonly Regex MultipleSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

        // ---------- § and ¶ ----------

        private static string SectionSigns(string text) => text
            .Replace("§§", " sections ")
            .Replace("§", " section ")
            .Replace("¶", " paragraph ");

        // ---------- abbreviations ----------

        private static readonly (Regex Pattern, string Replacement)[] AbbreviationRules =
        {
            (new(@"\bFigs\.?(?=\s*\d)"), "Figures"),
            (new(@"\bFig\.?(?=\s*\d)"), "Figure"),
            (new(@"\bpp\.\s*(?=\d)"), "pages "),
            (new(@"\bp\.\s*(?=\d)"), "page "),
            (new(@"\bEqs\.(?=\s*\(?\d)"), "Equations"),
            (new(@"\bEq\.(?=\s*\(?\d)"), "Equation"),
            (new(@"\bNos\.(?=\s*\d)"), "numbers"),
            (new(@"\bNo\.(?=\s*\d)"), "number"),
            (new(@"\bVol\.(?=\s*\d)"), "Volume"),
            (new(@"\bCh(?:ap)?\.(?=\s*\d)"), "Chapter"),
            (new(@"\bSect?\.(?=\s*\d)"), "Section"),
            (new(@"\bArt\.(?=\s*\d)"), "Article"),
            (new(@"\bTbl\.(?=\s*\d)"), "Table"),
            (new(@"\bRef\.(?=\s*\d)"), "Reference"),
            (new(@"\be\.g\.", RegexOptions.IgnoreCase), "for example"),
            (new(@"\bi\.e\.", RegexOptions.IgnoreCase), "that is"),
            (new(@"\betc\.(?=\s+[a-z])"), "et cetera"),
            (new(@"\betc\."), "et cetera."),
            (new(@"\bvs?\.(?=\s)"), "versus"),
            (new(@"\bapprox\."), "approximately"),
            (new(@"(?<!\d\s*)\bmax\.(?=\s)"), "maximum"),
            (new(@"(?<!\d\s*)\bmin\.(?=\s+[a-z])"), "minimum"),
            (new(@"\bMon\.(?=[\s,–-])"), "Monday"), (new(@"\bTue(?:s)?\.(?=[\s,–-])"), "Tuesday"),
            (new(@"\bWed\.(?=[\s,–-])"), "Wednesday"), (new(@"\bThu(?:rs?)?\.(?=[\s,–-])"), "Thursday"),
            (new(@"\bFri\.(?=[\s,–-]|$)"), "Friday"), (new(@"\bSat\.(?=[\s,–-]|$)"), "Saturday"),
            (new(@"\bSun\.(?=[\s,–-]|$)"), "Sunday"),
            (new(@"(?<=day)\s?[–-]\s?(?=\w+day\b)"), " to "),
            // "Class I", "Part I": a lone Roman one after a label is the number 1.
            (new(@"\b(Chapter|Part|Section|Volume|Appendix|Book|Unit|Phase|Type|Class|Division|Article|Level|Grade|Title|Schedule)\s+I\b(?!')"), "$1 1"),
            (new(@"\bDr\.(?=\s+[A-Z])"), "Doctor"),
            (new(@"\bMr\.(?=\s+[A-Z])"), "Mister"),
            (new(@"\bMrs\.(?=\s+[A-Z])"), "Missus"),
            (new(@"\bMs\.(?=\s+[A-Z])"), "Miz"),
        };

        private static readonly Regex MorningTime = new(@"(?<=\d)\s?(?:AM|am|a\.m\.)(?!\p{L})", RegexOptions.Compiled);
        private static readonly Regex EveningTime = new(@"(?<=\d)\s?(?:PM|pm|p\.m\.)(?!\p{L})", RegexOptions.Compiled);

        private static string Abbreviations(string text)
        {
            foreach (var (pattern, replacement) in AbbreviationRules)
                text = pattern.Replace(text, replacement);
            return text;
        }

        // ---------- 3/4" and 6' ----------

        private static readonly Regex InchMark = new(@"(?<=\d)\s?(?:""|″)(?![\p{L}\d])", RegexOptions.Compiled);
        private static readonly Regex FootMark = new(@"(?<=\d)\s?(?:'|′)(?![\p{L}\d])", RegexOptions.Compiled);

        private static string InchAndFootMarks(string text)
        {
            // A straight quote after a number is an inch mark only when it isn't
            // closing a quotation ("Chapter 5" must not become "5 inches").
            // Earlier inch marks (quotes right after a digit) aren't counted.
            text = InchMark.Replace(text, m =>
            {
                var quotesBefore = 0;
                for (int i = 0; i < m.Index; i++)
                    if (text[i] == '"' && (i == 0 || !char.IsDigit(text[i - 1])))
                        quotesBefore++;
                return m.Value.Contains('"') && quotesBefore % 2 == 1
                    ? m.Value
                    : " " + Plural(text, m.Index, "inch", "inches");
            });
            return FootMark.Replace(text, m => " " + Plural(text, m.Index, "foot", "feet"));
        }

        // ---------- units after numbers ----------

        private record Unit(string Singular, string Plural);

        // Keys are matched case-sensitively, longest first, right after a number.
        private static readonly Dictionary<string, Unit> UnitTable = BuildUnits();

        private static Dictionary<string, Unit> BuildUnits()
        {
            var u = new Dictionary<string, Unit>(StringComparer.Ordinal);
            void Add(string names, string singular, string? plural = null)
            {
                foreach (var name in names.Split('|'))
                    u[name] = new Unit(singular, plural ?? singular + "s");
            }

            // Electrical
            Add("V|volts|volt", "volt");
            Add("kV", "kilovolt"); Add("mV", "millivolt");
            Add("A|amps|amp", "amp"); Add("mA", "milliamp"); Add("kA", "kiloamp");
            Add("W", "watt"); Add("kW", "kilowatt"); Add("MW", "megawatt");
            Add("Wh", "watt hour"); Add("kWh", "kilowatt hour");
            Add("VA", "volt-amp"); Add("kVA", "kilovolt-amp");
            Add("Hz", "hertz", "hertz"); Add("kHz", "kilohertz", "kilohertz");
            Add("MHz", "megahertz", "megahertz"); Add("GHz", "gigahertz", "gigahertz");
            Add("Ω|ohm|ohms", "ohm"); Add("kΩ", "kilohm"); Add("MΩ", "megohm");
            Add("µF|μF|uF", "microfarad"); Add("nF", "nanofarad"); Add("pF", "picofarad");
            Add("mH", "millihenry", "millihenries");
            Add("kcmil", "K C mil", "K C mil");
            // Force and torque
            Add("lb-in|lb·in|lb.-in.|in-lb|in-lbs|in.-lb.", "pound-inch", "pound-inches");
            Add("ft-lb|ft-lbs|ft·lb|ft.-lb.|lb-ft", "foot-pound");
            Add("N·m|N-m|Nm", "newton meter");
            Add("lbf", "pound-force", "pounds-force");
            Add("lb|lbs|lb.|lbs.", "pound");
            Add("oz|oz.", "ounce");
            Add("kg", "kilogram"); Add("g", "gram"); Add("mg", "milligram");
            Add("hp|HP", "horsepower", "horsepower");
            // Length, area, volume
            Add("in.|inch|inches", "inch", "inches");
            Add("ft|ft.", "foot", "feet");
            Add("yd|yd.", "yard");
            Add("mi", "mile");
            Add("mm", "millimeter"); Add("cm", "centimeter"); Add("m", "meter"); Add("km", "kilometer");
            Add("µm|μm", "micrometer");
            Add("sq ft|sq. ft.|sq.ft.|ft²|ft2", "square foot", "square feet");
            Add("sq in|sq. in.|in²", "square inch", "square inches");
            Add("m²", "square meter"); Add("mm²", "square millimeter");
            Add("cu ft|cu. ft.|ft³", "cubic foot", "cubic feet");
            Add("cu yd|cu. yd.|yd³", "cubic yard");
            Add("m³", "cubic meter");
            Add("gal|gal.", "gallon"); Add("qt|qt.", "quart");
            Add("L", "liter"); Add("mL|ml", "milliliter");
            // Pressure and flow
            Add("psi|PSI", "P S I", "P S I"); Add("psig", "P S I G", "P S I G");
            Add("kPa", "kilopascal"); Add("MPa", "megapascal"); Add("Pa", "pascal");
            Add("gpm|GPM", "gallon per minute", "gallons per minute");
            Add("cfm|CFM", "cubic foot per minute", "cubic feet per minute");
            Add("rpm|RPM", "R P M", "R P M");
            Add("mph", "mile per hour", "miles per hour");
            Add("km/h", "kilometer per hour", "kilometers per hour");
            Add("BTU|Btu", "B T U", "B T U");
            Add("BTU/h|Btu/h|BTUH|Btuh", "B T U per hour", "B T U per hour");
            // Temperature (° followed by F or C)
            Add("°F|ºF", "degree Fahrenheit", "degrees Fahrenheit");
            Add("°C|ºC", "degree Celsius", "degrees Celsius");
            Add("°|º", "degree");
            Add("%", "percent", "percent");
            // Time
            Add("ms", "millisecond"); Add("sec|sec.", "second");
            Add("min|min.", "minute"); Add("hr|hr.|hrs|hrs.", "hour");
            return u;
        }

        // A number (with optional decimals, thousands separators, or a fraction
        // like "1-1/2" or "3/4") optionally followed by a range ("10-20", "10 to 20").
        private const string NumberPattern = @"\d+(?:[.,]\d+)*(?:[ -]\d+/\d+|/\d+)?";

        private static readonly Regex UnitAfterNumber = new(
            @"(?<![\p{L}\d.,/])(?<num>" + NumberPattern + @")" +
            @"(?:\s?(?:-|–|to)\s?(?<num2>" + NumberPattern + @"))?" +
            @"(?:\s?)(?<unit>" + string.Join('|', UnitTable.Keys
                .OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")" +
            // The unit must not run into more letters ("5 men" is not meters).
            @"(?![\p{L}\d²³])",
            RegexOptions.Compiled);

        private static string Units(string text) => UnitAfterNumber.Replace(text, m =>
        {
            var unitText = m.Groups["unit"].Value;
            var unit = UnitTable[unitText];
            var hasRange = m.Groups["num2"].Success;
            var lastNumber = hasRange ? m.Groups["num2"].Value : m.Groups["num"].Value;
            // "15 A" is amps, but "Class 1 A" or "Figure 3 A" is a letter; only
            // treat single capital letters as units when glued on ("15A") or
            // when a sentence context isn't obviously a label.
            if (unitText is "A" or "V" or "W" or "L" or "m" or "g" &&
                LooksLikeLabel(text, m.Index))
                return m.Value;

            var spokenNumber = hasRange ? $"{m.Groups["num"].Value} to {lastNumber}" : lastNumber;
            var unitWords = !hasRange && (IsOneOrLess(lastNumber) || AfterArticle(text, m.Index))
                ? unit.Singular : unit.Plural;
            // Keep a sentence-ending period that was part of an abbreviation ("5 in.").
            var period = unitText.EndsWith('.') && EndsSentence(text, m.Index + m.Length) ? "." : "";
            return $"{spokenNumber} {unitWords}{period}";
        });

        // "Class 2 A", "Type 3 V", "Figure 4 A", "Table 2 W", "page 5 A"
        private static readonly Regex LabelWord = new(
            @"(?:Class|Type|Figure|Fig\.?|Table|Group|Division|Zone|Grade|Size|Item|Step|Part|page|Chapter|Section|Article|Appendix|Volume|Exhibit)\s+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool LooksLikeLabel(string text, int numberStart) =>
            LabelWord.IsMatch(text.AsSpan(0, numberStart).ToString());

        private static bool EndsSentence(string text, int index)
        {
            var rest = text.AsSpan(index).TrimStart();
            return rest.IsEmpty || char.IsUpper(rest[0]);
        }

        // ---------- 210.8(A)(1) ----------

        private static readonly Regex SectionReference = new(
            @"(?<=\d)((?:\((?:[A-Za-z]|\d{1,3}|[ivx]{1,4})\))+)", RegexOptions.Compiled);
        private static readonly Regex ParenthesizedPart = new(@"\(([^)]*)\)", RegexOptions.Compiled);

        private static string SectionReferences(string text) => SectionReference.Replace(text, m =>
            ParenthesizedPart.Replace(m.Value, p =>
            {
                var part = p.Groups[1].Value;
                return " " + (part.Length == 1 && char.IsLetter(part[0]) ? part.ToUpperInvariant() : part);
            }));

        // ---------- 3/4, 1-1/2 ----------

        private static readonly Regex MixedFraction = new(
            @"(?<![\d/.])(\d+)[ -](\d+)/(\d+)(?![\d/])", RegexOptions.Compiled);
        private static readonly Regex SimpleFraction = new(
            @"(?<![\d/.])(\d+)/(\d+)(?![\d/])", RegexOptions.Compiled);

        private static readonly Dictionary<int, (string One, string Many)> Denominators = new()
        {
            [2] = ("half", "halves"), [3] = ("third", "thirds"), [4] = ("quarter", "quarters"),
            [8] = ("eighth", "eighths"), [16] = ("sixteenth", "sixteenths"),
            [32] = ("thirty-second", "thirty-seconds"), [64] = ("sixty-fourth", "sixty-fourths"),
        };

        private static string? SpeakFraction(string numerator, string denominator, bool afterWhole)
        {
            if (!int.TryParse(numerator, out var n) || !int.TryParse(denominator, out var d) ||
                !Denominators.TryGetValue(d, out var names) || n <= 0 || n >= d)
                return null; // dates like 12/25 and other ratios are left alone
            if (n == 1)
                return (afterWhole ? (d == 8 ? "an " : "a ") : "one ") + names.One;
            return $"{n} {names.Many}";
        }

        private static string Fractions(string text)
        {
            text = MixedFraction.Replace(text, m =>
                SpeakFraction(m.Groups[2].Value, m.Groups[3].Value, afterWhole: true) is { } f
                    ? $"{m.Groups[1].Value} and {f}"
                    : m.Value);
            return SimpleFraction.Replace(text, m =>
                SpeakFraction(m.Groups[1].Value, m.Groups[2].Value, afterWhole: false) ?? m.Value);
        }

        // ---------- 10–20, pages 4-7, 2x4 ----------

        private static readonly Regex EnDashRange = new(@"(?<=\d)\s?–\s?(?=\d)", RegexOptions.Compiled);
        private static readonly Regex PageRange = new(@"\b(pages|Figures|Equations|Sections|Chapters)\s+(\d+)\s?-\s?(\d+)\b", RegexOptions.Compiled);
        private static readonly Regex Dimensions = new(@"(?<=\d)\s?[x×]\s?(?=\d)", RegexOptions.Compiled);

        private static string Ranges(string text)
        {
            text = PageRange.Replace(text, "$1 $2 to $3");
            return EnDashRange.Replace(text, " to ");
        }

        // ---------- acronyms ----------

        // Known words that, in technical text, are almost always initialisms.
        private static readonly HashSet<string> AlwaysSpell = new(StringComparer.Ordinal)
        {
            "AC", "DC", "LED", "LEDs", "PSI", "ID", "OD", "PVC", "EMT", "IMC", "RMC", "PE", "PPE",
            "CO", "GPM", "CPR", "AED", "SDS", "MSDS", "UL", "TV", "PC", "UK", "HP", "CAD",
        };

        // Acronyms said as words, with a spelling the voice reads correctly.
        private static readonly Dictionary<string, string> SayAsWord = new(StringComparer.Ordinal)
        {
            ["OSHA"] = "osha", ["NEMA"] = "neema", ["ANSI"] = "ansi", ["ASHRAE"] = "ashray",
            ["NIOSH"] = "nyosh", ["HIPAA"] = "hippa", ["FEMA"] = "feema", ["NASA"] = "nasa",
            ["NATO"] = "nato", ["SCUBA"] = "scuba", ["LASER"] = "laser", ["RADAR"] = "radar",
            ["SONAR"] = "sonar", ["ASAP"] = "A S A P", ["HVAC"] = "H vac", ["GIF"] = "gif",
            ["JPEG"] = "jay peg", ["SCADA"] = "skayda", ["NFPA"] = "N F P A", ["AWOL"] = "awol",
            ["UNESCO"] = "unesco", ["UNICEF"] = "unicef", ["WIFI"] = "wifi", ["COVID"] = "covid",
        };

        // Common words that show up in capitals in headings ("NOTE", "WARNING");
        // only used when the voice's dictionary isn't available.
        private static readonly HashSet<string> CommonCapsWords = new(StringComparer.Ordinal)
        {
            "A", "I", "AN", "AND", "ARE", "AS", "AT", "BE", "BY", "DO", "FOR", "GO", "IF", "IN", "IS",
            "NO", "NOT", "OF", "ON", "OR", "SO", "THE", "TO", "UP", "WE", "YOU", "ALL", "ANY", "CAN",
            "NEW", "NOW", "OFF", "OUT", "USE", "MAY", "ONE", "TWO", "SEE", "END", "TOP", "KEY", "LOW",
            "HIGH", "NOTE", "STOP", "WARNING", "DANGER", "CAUTION", "NOTICE", "SAFETY", "STEP", "PART",
        };

        private static readonly Regex CapitalWord = new(
            @"(?<![\p{L}\d'])([A-Z][A-Z0-9]*[A-Z0-9])(s?)(?![\p{L}\d])", RegexOptions.Compiled);

        private static readonly Regex RomanNumeral = new(
            @"^(?=[IVXLC])C{0,3}(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.Compiled);

        // Words after which a Roman numeral is a number: "Chapter IV", "Class II".
        private static readonly Regex RomanLabel = new(
            @"(?:Chapter|Part|Section|Volume|Appendix|Book|Unit|Phase|Type|Class|Division|Article|Level|Grade|Title|Schedule|War)\s+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static int RomanToNumber(string roman)
        {
            static int Value(char c) => c switch { 'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, _ => 100 };
            int total = 0;
            for (int i = 0; i < roman.Length; i++)
            {
                int v = Value(roman[i]);
                total += i + 1 < roman.Length && Value(roman[i + 1]) > v ? -v : v;
            }
            return total;
        }

        private string Acronyms(string text) => CapitalWord.Replace(text, m =>
        {
            var word = m.Groups[1].Value;
            var plural = m.Groups[2].Value.Length > 0;
            if (!word.Any(char.IsLetter) || word.Count(char.IsLetter) < 2 && !word.Any(char.IsDigit))
                return m.Value;

            if (!plural && RomanNumeral.IsMatch(word))
                return RomanLabel.IsMatch(text[..m.Index]) ? RomanToNumber(word).ToString() : m.Value;
            if (SayAsWord.TryGetValue(word, out var spoken))
                return spoken + (plural ? "s" : "");
            if (!AlwaysSpell.Contains(word) && !AlwaysSpell.Contains(m.Value) && IsWord(m.Value, word))
                return m.Value;

            return Spell(word) + (plural ? "'s" : "");
        });

        private bool IsWord(string whole, string stem)
        {
            if (stem.Any(char.IsDigit))
                return false;
            var known = _isKnownWord(whole.ToLowerInvariant()) ?? _isKnownWord(stem.ToLowerInvariant());
            if (known.HasValue)
                return known.Value;
            // No dictionary: long capital words are probably shouted headings.
            return CommonCapsWords.Contains(stem) || stem.Length >= 5;
        }

        // "GFCI" → "G F C I", "R22" → "R 22", "NEC2023" → "N E C 2023"
        private static string Spell(string word)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < word.Length; i++)
            {
                if (char.IsDigit(word[i]))
                {
                    int start = i;
                    while (i + 1 < word.Length && char.IsDigit(word[i + 1])) i++;
                    sb.Append(' ').Append(word, start, i - start + 1);
                }
                else
                {
                    sb.Append(' ').Append(word[i]);
                }
            }
            return sb.ToString().Trim();
        }

        // ---------- symbols in running text ----------

        private static readonly Regex Superscript = new(@"(?<=[\p{L}\d)])([²³⁴⁵⁶⁷⁸⁹⁰¹]+)", RegexOptions.Compiled);
        private static readonly Regex Subscript = new(@"(?<=\p{L})([₀₁₂₃₄₅₆₇₈₉]+)", RegexOptions.Compiled);
        private static readonly Regex SpacedOperator = new(@"(?<=[\s\d])([=+])(?=[\s\d])", RegexOptions.Compiled);

        // Symbols safe to read anywhere. Plain ASCII (-, /, *, <, >, !) is left
        // to the voice because it is too common in ordinary prose.
        private const string ProseSymbols = "≤≥≠≈≅≡±∓×÷√∛∞∂∇∑∏∫→⇒⇔∈∉⊂⊆∪∩∅∀∃∠△⊥∥" +
                                            "αβγδεζηθικλμνξπρστυφχψωΓΔΘΛΞΠΣΦΨΩ";

        private static string Symbols(string text)
        {
            text = Superscript.Replace(text, m => m.Value switch
            {
                "²" => " squared",
                "³" => " cubed",
                _ => " to the power of " + ToDigits(m.Value),
            });
            text = Subscript.Replace(text, m => " " + ToDigits(m.Value) + " ");
            text = SpacedOperator.Replace(text, m => m.Value == "=" ? " equals " : " plus ");

            if (text.AsSpan().IndexOfAny(ProseSymbols) < 0)
                return text;
            var sb = new StringBuilder(text.Length + 32);
            foreach (var c in text)
            {
                if (ProseSymbols.Contains(c) && MathSymbols.Word(c) is { } word)
                    sb.Append(' ').Append(word).Append(' ');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ToDigits(string scripts)
        {
            const string sup = "⁰¹²³⁴⁵⁶⁷⁸⁹", sub = "₀₁₂₃₄₅₆₇₈₉";
            return new string(scripts.Select(c =>
                sup.IndexOf(c) is >= 0 and var i ? (char)('0' + i)
                : sub.IndexOf(c) is >= 0 and var j ? (char)('0' + j) : c).ToArray());
        }

        // ---------- number helpers ----------

        private static readonly Regex TrailingNumber = new(@"(" + NumberPattern + @")\s*$", RegexOptions.Compiled);

        // The unit after "a" or "an" describes something: "a 6 foot ladder",
        // "a 90 degree bend", so it stays singular.
        private static readonly Regex Article = new(@"\b(?:a|an)\s+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool AfterArticle(string text, int numberStart) =>
            Article.IsMatch(text.AsSpan(0, numberStart).ToString());

        /// <summary>Singular or plural for the number that ends just before <paramref name="markIndex"/>.</summary>
        private static string Plural(string text, int markIndex, string one, string many)
        {
            var match = TrailingNumber.Match(text[..markIndex]);
            if (!match.Success)
                return many;
            var number = match.Groups[1].Value;
            return IsOneOrLess(number) || AfterArticle(text, match.Groups[1].Index) ? one : many;
        }

        /// <summary>"1", "0.5", "3/4" take a singular unit; everything else plural.</summary>
        private static bool IsOneOrLess(string number)
        {
            number = number.Trim();
            if (number.Contains('/'))
            {
                var parts = number.Split('/');
                // Only real fractions (3/4); "120/240" is a pair of values.
                return !number.Contains(' ') && !number.Contains('-') &&
                       int.TryParse(parts[0], out var n) && int.TryParse(parts[1], out var d) &&
                       Denominators.ContainsKey(d) && n <= d;
            }
            return number == "1";
        }
    }
}
