using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TTSFreeUse.Core.Extractors
{
    public class PdfExtractor : ITextExtractor
    {
        // How many lines at the top and bottom of each page are considered
        // candidates for running headers/footers.
        private const int EdgeLines = 2;

        // A line counts as a running header/footer when it appears (ignoring
        // digits) on at least this fraction of pages.
        private const double RepeatThreshold = 0.5;
        private const int MinPagesForRepeatDetection = 3;

        private static readonly Regex PageNumber = new(
            @"^\s*(page\s*)?(\d+|(?=[ivxlcdm])m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3}))(\s*(of|/)\s*\d+)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

        public bool CanHandle(string ext) => ext == ".pdf";

        public Task<string> ExtractAsync(Stream fileStream)
        {
            using var doc = PdfDocument.Open(fileStream);

            // ContentOrderTextExtractor keeps line breaks, which the cleaner
            // needs to rejoin hyphenated words and find paragraph boundaries.
            var pages = doc.GetPages()
                .Select(p => ContentOrderTextExtractor.GetText(p)
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .ToList())
                .ToList();

            var repeated = FindRepeatedEdgeLines(pages);

            var pageTexts = pages.Select(lines => string.Join("\n",
                lines.Where((line, i) => !IsEdgeNoise(line, i, lines.Count, repeated))));

            // Pages are joined with a single newline: paragraphs often continue
            // across a page break, so a page boundary is not a paragraph break.
            return Task.FromResult(string.Join("\n", pageTexts));
        }

        private static bool IsEdge(int index, int count) =>
            index < EdgeLines || index >= count - EdgeLines;

        private static bool IsEdgeNoise(string line, int index, int count, HashSet<string> repeated)
        {
            if (!IsEdge(index, count))
                return false;
            return PageNumber.IsMatch(line) || repeated.Contains(Normalize(line));
        }

        private static HashSet<string> FindRepeatedEdgeLines(List<List<string>> pages)
        {
            var result = new HashSet<string>();
            if (pages.Count < MinPagesForRepeatDetection)
                return result;

            var counts = new Dictionary<string, int>();
            foreach (var lines in pages)
            {
                // Count each distinct line once per page.
                var edge = lines
                    .Where((_, i) => IsEdge(i, lines.Count))
                    .Select(Normalize)
                    .Where(l => l.Length > 0)
                    .Distinct();
                foreach (var key in edge)
                    counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            var threshold = pages.Count * RepeatThreshold;
            foreach (var (key, count) in counts)
                if (count >= threshold)
                    result.Add(key);
            return result;
        }

        // Page numbers inside headers ("Chapter 3 — 41") vary per page, so
        // compare lines with the digits masked out.
        private static string Normalize(string line) =>
            Digits.Replace(line.Trim(), "#").ToLowerInvariant();
    }
}
