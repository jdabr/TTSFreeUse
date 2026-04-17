using System;
namespace DocToSpeech.Core.Transformers
{
    public class TextChunker
    {
        private const int MaxChunkLength = 800;

        public List<string> Chunk(string text)
        {
            var chunks = new List<string>();

            // Split on sentence endings
            var sentences = text.Split(new[] { ". ", "! ", "? " },
                StringSplitOptions.RemoveEmptyEntries);

            var current = string.Empty;

            foreach (var sentence in sentences)
            {
                // If adding this sentence exceeds the limit, save current chunk
                if (current.Length + sentence.Length > MaxChunkLength
                    && current.Length > 0)
                {
                    chunks.Add(current.Trim());
                    current = string.Empty;
                }

                current += sentence + ". ";
            }

            // Add any remaining text
            if (!string.IsNullOrWhiteSpace(current))
                chunks.Add(current.Trim());

            return chunks;
        }
    }
}
