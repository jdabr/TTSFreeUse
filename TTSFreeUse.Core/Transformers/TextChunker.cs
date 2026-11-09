using System.Text;
using System.Text.RegularExpressions;

namespace TTSFreeUse.Core.Transformers
{
    /// <summary>
    /// Splits cleaned text into chunks small enough for a single TTS request,
    /// breaking at paragraph and sentence boundaries where possible and never
    /// altering the text itself.
    /// </summary>
    public class TextChunker
    {
        public int MaxChunkLength { get; init; } = 800;

        // Split after sentence-ending punctuation (optionally followed by a
        // closing quote/bracket), keeping the punctuation with its sentence.
        private static readonly Regex SentenceBoundary =
            new(@"(?<=[.!?…][""')\]]?)\s+", RegexOptions.Compiled);

        // Fallback split points for sentences that are too long on their own.
        private static readonly Regex ClauseBoundary =
            new(@"(?<=[,;:—])\s+", RegexOptions.Compiled);

        public List<string> Chunk(string text)
        {
            var chunks = new List<string>();
            var current = new StringBuilder();

            void Flush()
            {
                if (current.Length > 0)
                    chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            // Sentences from consecutive paragraphs are packed into the same
            // chunk, so short paragraphs don't each become a separate request.
            foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var sentence in Sentences(paragraph.Trim()))
                {
                    foreach (var piece in SplitLong(sentence))
                    {
                        if (current.Length > 0 && current.Length + 1 + piece.Length > MaxChunkLength)
                            Flush();
                        if (current.Length > 0)
                            current.Append(' ');
                        current.Append(piece);
                    }
                }
            }

            Flush();
            return chunks.Where(c => c.Length > 0).ToList();
        }

        // A period after these isn't the end of a sentence: "See Fig. 3",
        // "on p. 47", "e.g. copper", "J. Smith".
        private static readonly Regex AbbreviationAtEnd = new(
            @"(?:^|[\s(])(?:Figs?|pp?|Eqs?|Nos?|Vol|Ch|Chap|Sec|Sect|Art|Tbl|Ref|e\.g|i\.e|vs?|approx|" +
            @"Dr|Mr|Mrs|Ms|St|Jr|Sr|Inc|Co|Corp|Ltd|[A-Z])\.$",
            RegexOptions.Compiled);

        private static IEnumerable<string> Sentences(string paragraph)
        {
            string? pending = null;
            foreach (var part in SentenceBoundary.Split(paragraph))
            {
                if (pending == null)
                    pending = part;
                // Keep "Fig." with "3-2", and "5 in." with "long" (a lowercase
                // or numeric start means the sentence didn't really end).
                else if (AbbreviationAtEnd.IsMatch(pending) ||
                         (part.Length > 0 && (char.IsLower(part[0]) || char.IsDigit(part[0]))))
                    pending += " " + part;
                else
                {
                    yield return pending;
                    pending = part;
                }
            }
            if (pending != null)
                yield return pending;
        }

        // Breaks an over-long sentence at clause punctuation, then at spaces,
        // and as a last resort (e.g. a giant URL) at the hard limit.
        private IEnumerable<string> SplitLong(string sentence)
        {
            if (sentence.Length <= MaxChunkLength)
            {
                yield return sentence;
                yield break;
            }

            foreach (var clause in ClauseBoundary.Split(sentence))
            {
                var rest = clause;
                while (rest.Length > MaxChunkLength)
                {
                    var cut = rest.LastIndexOf(' ', MaxChunkLength);
                    if (cut <= 0)
                        cut = MaxChunkLength;
                    yield return rest[..cut].Trim();
                    rest = rest[cut..].Trim();
                }
                if (rest.Length > 0)
                    yield return rest;
            }
        }
    }
}
