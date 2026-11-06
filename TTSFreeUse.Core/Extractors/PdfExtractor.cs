using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Tokens;

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

        // Arabic page numbers, or Roman numerals up to 399 (front matter never
        // runs longer). Excluding M and D keeps words like "mix", "mid" and
        // "dim" — all valid numerals — from being mistaken for page numbers.
        private static readonly Regex PageNumber = new(
            @"^\s*(page\s*)?(\d+|(?=[ivxlc])c{0,3}(xc|xl|l?x{0,3})(ix|iv|v?i{0,3}))(\s*(of|/)\s*\d+)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

        public bool CanHandle(string ext) => ext == ".pdf";

        public Task<string> ExtractAsync(Stream fileStream)
        {
            PdfDocument opened;
            try
            {
                opened = PdfDocument.Open(fileStream);
            }
            catch (PdfDocumentEncryptedException)
            {
                throw new ProtectedDocumentException(
                    "This PDF is password-protected. TTSFreeUse can only read files that open without a password.");
            }

            using var doc = opened;
            if (doc.IsEncrypted)
                EnsureAccessibilityExtractionAllowed(doc);

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

        // PDF permission flags (the /P entry; bits are numbered from 1).
        private const long PermitCopy = 1 << 4;                    // bit 5
        private const long PermitAccessibilityExtraction = 1 << 9; // bit 10

        /// <summary>
        /// Honors the publisher's restrictions on encrypted PDFs that open
        /// without a password. Text is read only when the publisher permits
        /// copying or, specifically, extraction for accessibility tools.
        /// DRM schemes other than the standard PDF security handler (e.g. Adobe
        /// ebook DRM) are always refused.
        /// </summary>
        private static void EnsureAccessibilityExtractionAllowed(PdfDocument doc)
        {
            var encrypt = doc.Structure.Trailer.EncryptionToken switch
            {
                DictionaryToken d => d,
                IndirectReferenceToken r => doc.Structure.GetObject(r.Data).Data as DictionaryToken,
                _ => null,
            };

            if (encrypt == null)
                throw CopyProtected();

            if (!encrypt.TryGet(NameToken.Filter, out NameToken filter) || filter.Data != "Standard")
                throw CopyProtected();

            // PDF 2.0 deprecates the accessibility bit: readers must treat
            // extraction for accessibility as always permitted.
            if (doc.Version >= 2.0)
                return;

            if (!encrypt.TryGet(NameToken.P, out NumericToken p))
                throw CopyProtected();

            var permissions = p.Long;
            if ((permissions & (PermitCopy | PermitAccessibilityExtraction)) == 0)
                throw new ProtectedDocumentException(
                    "The publisher of this PDF has turned off text access, including for accessibility " +
                    "tools, so TTSFreeUse can't read it. Check whether the publisher offers an accessible " +
                    "edition (many do on request).");
        }

        private static ProtectedDocumentException CopyProtected() => new(
            "This PDF is copy-protected (DRM). TTSFreeUse doesn't bypass copy protection. " +
            "Many ebook stores and apps include their own read-aloud feature for protected books.");

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
