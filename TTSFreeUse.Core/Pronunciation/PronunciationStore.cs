using System.Text.Json;
using System.Text.RegularExpressions;

namespace TTSFreeUse.Core.Pronunciation
{
    /// <summary>One user rule: whenever <see cref="Term"/> appears, say <see cref="SayAs"/>.</summary>
    /// <param name="Term">The word or phrase as written, e.g. "NEC" or "Romex".</param>
    /// <param name="SayAs">What the voice should say instead, e.g. "N E C" or "roh mex".</param>
    /// <param name="MatchCase">Only replace when the capitalization matches exactly.</param>
    public record PronunciationEntry(string Term, string SayAs, bool MatchCase = false);

    /// <summary>
    /// The user's pronunciation list, saved as JSON on this computer
    /// (default <c>%LOCALAPPDATA%\TTSFreeUse\pronunciations.json</c>).
    /// Whole words and phrases only, so "AC" never changes "ACT".
    /// </summary>
    public class PronunciationStore
    {
        public const int MaxEntries = 2000;
        public const int MaxTermLength = 100;
        public const int MaxSayAsLength = 300;

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        private readonly object _saveLock = new();
        private volatile Snapshot _snapshot = Snapshot.Empty;

        public PronunciationStore(string? path = null)
        {
            FilePath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            Load();
        }

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTSFreeUse", "pronunciations.json");

        public string FilePath { get; }

        /// <summary>Set when the saved file couldn't be read; the list starts empty.</summary>
        public string? LoadError { get; private set; }

        public IReadOnlyList<PronunciationEntry> Entries => _snapshot.Entries;

        /// <summary>Validates, saves, and starts using a new list.</summary>
        /// <exception cref="ArgumentException">With a message suitable for the user.</exception>
        public void Replace(IEnumerable<PronunciationEntry> entries)
        {
            var cleaned = Validate(entries);
            lock (_saveLock)
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                // Write to a temp file and swap, so a crash can't leave half a file.
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(cleaned, Json));
                File.Move(temp, FilePath, overwrite: true);
                _snapshot = Snapshot.Build(cleaned);
                LoadError = null;
            }
        }

        /// <summary>Applies the list to <paramref name="text"/>.</summary>
        public string Apply(string text) => _snapshot.Apply(text);

        private void Load()
        {
            if (!File.Exists(FilePath))
                return;
            try
            {
                var entries = JsonSerializer.Deserialize<List<PronunciationEntry>>(File.ReadAllText(FilePath)) ?? new();
                _snapshot = Snapshot.Build(Validate(entries));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException)
            {
                LoadError = $"Couldn't read the pronunciation list at {FilePath}: {ex.Message}";
            }
        }

        private static List<PronunciationEntry> Validate(IEnumerable<PronunciationEntry> entries)
        {
            var result = new List<PronunciationEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var term = (entry.Term ?? "").Trim();
                var sayAs = (entry.SayAs ?? "").Trim();
                if (term.Length == 0)
                    continue; // blank rows from the editor
                if (term.Length > MaxTermLength)
                    throw new ArgumentException($"\"{term[..20]}…\" is too long (max {MaxTermLength} characters).");
                if (sayAs.Length > MaxSayAsLength)
                    throw new ArgumentException($"What to say for \"{term}\" is too long (max {MaxSayAsLength} characters).");
                if (!seen.Add(entry.MatchCase ? "case:" + term : term))
                    throw new ArgumentException($"\"{term}\" is in the list twice.");
                result.Add(new PronunciationEntry(term, sayAs, entry.MatchCase));
            }
            if (result.Count > MaxEntries)
                throw new ArgumentException($"The list can have at most {MaxEntries} words.");
            return result;
        }

        private sealed class Snapshot
        {
            public static readonly Snapshot Empty = new(Array.Empty<PronunciationEntry>(), null);

            private readonly Regex? _pattern;
            private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _anyCase = new(StringComparer.OrdinalIgnoreCase);

            private Snapshot(IReadOnlyList<PronunciationEntry> entries, Regex? pattern)
            {
                Entries = entries;
                _pattern = pattern;
                foreach (var e in entries)
                    (e.MatchCase ? _exact : _anyCase)[e.Term] = e.SayAs;
            }

            public IReadOnlyList<PronunciationEntry> Entries { get; }

            public static Snapshot Build(List<PronunciationEntry> entries)
            {
                if (entries.Count == 0)
                    return Empty;
                // Longest first, so "lb-in" wins over "lb". Terms are matched as
                // whole words: not inside a longer word or number.
                var alternatives = entries.Select(e => e.Term).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(t => t.Length).Select(Regex.Escape);
                var pattern = new Regex(
                    @"(?<![\p{L}\p{N}_])(?:" + string.Join('|', alternatives) + @")(?![\p{L}\p{N}_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                return new Snapshot(entries, pattern);
            }

            public string Apply(string text)
            {
                if (_pattern == null)
                    return text;
                try
                {
                    return _pattern.Replace(text, m =>
                        _exact.TryGetValue(m.Value, out var exact) ? exact
                        : _anyCase.TryGetValue(m.Value, out var any) ? any
                        : m.Value); // a match-case term with different capitalization
                }
                catch (RegexMatchTimeoutException)
                {
                    return text;
                }
            }
        }
    }
}
