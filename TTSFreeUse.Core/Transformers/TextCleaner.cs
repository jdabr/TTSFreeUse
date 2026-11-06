using System.Text;
using System.Text.RegularExpressions;

namespace TTSFreeUse.Core.Transformers
{
    /// <summary>
    /// Normalizes extracted text for speech. Output is plain paragraphs
    /// separated by a blank line ("\n\n"), with no other line breaks.
    /// </summary>
    public class TextCleaner
    {
        private static readonly Dictionary<char, string> Replacements = new()
        {
            // Smart quotes -> straight quotes
            ['“'] = "\"", ['”'] = "\"", ['„'] = "\"",
            ['‘'] = "'", ['’'] = "'", ['‚'] = "'",
            // Typographic ligatures common in PDFs ("ﬁle" -> "file")
            ['ﬀ'] = "ff", ['ﬁ'] = "fi", ['ﬂ'] = "fl",
            ['ﬃ'] = "ffi", ['ﬄ'] = "ffl",
            // Non-breaking and other special spaces -> regular space
            [' '] = " ", [' '] = " ", [' '] = " ",
            // Invisible characters that only confuse TTS engines
            ['­'] = "", // soft hyphen
            ['​'] = "", ['‌'] = "", ['‍'] = "", ['﻿'] = "",
        };

        // "compu-\nter" -> "computer". Only joins when the next line starts
        // lowercase, so "COVID-\n19" or list items are left alone.
        private static readonly Regex HyphenatedLineBreak =
            new(@"(\p{L})-[ \t]*\n[ \t]*(\p{Ll})", RegexOptions.Compiled);

        // Any other hyphen at a line end ("COVID-\n19") keeps the hyphen but
        // drops the line break, so it isn't read as "COVID- 19".
        private static readonly Regex HyphenAtLineEnd =
            new(@"-[ \t]*\n[ \t]*(?=\S)", RegexOptions.Compiled);

        private static readonly Regex ParagraphBreak =
            new(@"[ \t]*\n[ \t]*(\n[ \t]*)+", RegexOptions.Compiled);

        private static readonly Regex HorizontalWhitespace =
            new(@"[^\S\n]+", RegexOptions.Compiled);

        public string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (Replacements.TryGetValue(c, out var replacement))
                    sb.Append(replacement);
                else if (char.IsControl(c) && c != '\n' && c != '\t')
                    sb.Append(' ');
                else
                    sb.Append(c); // keep accents, dashes, math symbols, etc.
            }

            var cleaned = HyphenatedLineBreak.Replace(sb.ToString(), "$1$2");
            cleaned = HyphenAtLineEnd.Replace(cleaned, "-");

            // Blank lines mark paragraphs; any remaining single newline is just
            // a wrapped line within a paragraph.
            var paragraphs = ParagraphBreak.Split(cleaned)
                .Select(p => HorizontalWhitespace.Replace(p.Replace('\n', ' '), " ").Trim())
                .Where(p => p.Length > 0);

            return string.Join("\n\n", paragraphs);
        }
    }
}
