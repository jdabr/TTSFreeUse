using TTSFreeUse.Core.Audio;

namespace TTSFreeUse.Core.Jobs
{
    public record LibraryItem(string FileName, long SizeBytes, DateTimeOffset Created, AudioFormat Format);

    /// <summary>
    /// The folder where finished narrations are saved, so they can be played
    /// again, downloaded, or copied to a phone. Only audio files directly in
    /// this folder are ever listed, opened, or deleted.
    /// </summary>
    public class AudioLibrary
    {
        public string Directory { get; }

        public AudioLibrary(string? directory = null)
        {
            Directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory() : directory;
        }

        /// <summary>Documents\TTSFreeUse on Windows, ~/Documents/TTSFreeUse elsewhere — easy to find.</summary>
        public static string DefaultDirectory()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs))
                docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(docs, "TTSFreeUse");
        }

        public IReadOnlyList<LibraryItem> List()
        {
            if (!System.IO.Directory.Exists(Directory))
                return Array.Empty<LibraryItem>();

            return new DirectoryInfo(Directory).EnumerateFiles()
                .Select(f => AudioFormats.TryFromExtension(f.Extension, out var format)
                    ? new LibraryItem(f.Name, f.Length, f.CreationTimeUtc, format)
                    : null)
                .OfType<LibraryItem>()
                .OrderByDescending(i => i.Created)
                .ToList();
        }

        /// <summary>
        /// Resolves a file name from a request to a path inside the library, or
        /// null if it isn't a plain audio file name (e.g. "..\secret.txt").
        /// </summary>
        public string? Resolve(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || fileName != Path.GetFileName(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !AudioFormats.TryFromExtension(Path.GetExtension(fileName), out _))
                return null;

            var path = Path.Combine(Directory, fileName);
            return File.Exists(path) ? path : null;
        }

        public bool Delete(string fileName)
        {
            var path = Resolve(fileName);
            if (path == null)
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// Picks an unused file name like "Electrical Code Ch 3.mp3", adding
        /// " (2)", " (3)"… if needed. Creates the library folder.
        /// </summary>
        public string ReservePath(string title, AudioFormat format)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var baseName = SafeName(title);
            var ext = format.Extension();

            for (int n = 1; ; n++)
            {
                var name = n == 1 ? baseName + ext : $"{baseName} ({n}){ext}";
                var path = Path.Combine(Directory, name);
                try
                {
                    // CreateNew makes the reservation atomic between concurrent jobs.
                    using (new FileStream(path, FileMode.CreateNew)) { }
                    return path;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }
        }

        public static string SafeName(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
            cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
            if (cleaned.Length > 80)
                cleaned = cleaned[..80].TrimEnd(' ', '.');
            return cleaned.Length > 0 ? cleaned : "Narration";
        }
    }
}
