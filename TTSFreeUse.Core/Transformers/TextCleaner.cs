using System;
using System.Text.RegularExpressions;

namespace DocToSpeech.Core.Transformers
{
    public class TextCleaner
    {
        public string Clean(string raw)
        {
            // Remove excess whitespace
            var cleaned = Regex.Replace(raw, @"\s+", " ");

            // Replace smart quotes with standard quotes
            cleaned = cleaned.Replace("\u201C", "\"").Replace("\u201D", "\"");

            // Replace smart apostrophes
            cleaned = cleaned.Replace("\u2018", "'").Replace("\u2019", "'");

            // Remove any non-printable characters
            cleaned = Regex.Replace(cleaned, @"[^\x20-\x7E]", " ");

            return cleaned.Trim();
        }
    }
}
