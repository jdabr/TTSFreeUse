using System.Text.RegularExpressions;
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
                || IsReservedName(fileName)
                || !AudioFormats.TryFromExtension(Path.GetExtension(fileName), out _))
                return null;

            var path = Path.Combine(Directory, fileName);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Deletes a file from the library. Returns false if there's no such
        /// file; throws <see cref="IOException"/> if another program has it open.
        /// </summary>
        public bool Delete(string fileName)
        {
            var path = Resolve(fileName);
            if (path == null)
                return false;
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// Moves a finished audio file into the library, named after
        /// <paramref name="title"/> ("Electrical Code Ch 3.mp3", or " (2)", " (3)"…
        /// if that's taken), and returns its new path. Creates the library folder.
        /// Files are only moved in once complete, so the library never lists a
        /// half-written one.
        /// </summary>
        public string Add(string finishedFile, string title, AudioFormat format)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var baseName = SafeName(title);
            var ext = format.Extension();

            for (int n = 1; n <= 10_000; n++)
            {
                var path = Path.Combine(Directory, n == 1 ? baseName + ext : $"{baseName} ({n}){ext}");
                if (File.Exists(path))
                    continue;
                try
                {
                    File.Move(finishedFile, path, overwrite: false);
                    return path;
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Another file took that name in the meantime; try the next.
                }
            }
            throw new IOException($"There are too many files named \"{baseName}\" in {Directory}.");
        }

        public static string SafeName(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
            cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
            if (cleaned.Length > 80)
                cleaned = cleaned[..80].TrimEnd(' ', '.');
            if (IsReservedName(cleaned))
                cleaned = "_" + cleaned;
            return cleaned.Length > 0 ? cleaned : "Narration";
        }

        // Names Windows 10 and earlier treat as devices rather than files, with
        // or without an extension: "NUL.mp3" would write to nowhere and
        // "COM1.mp3" to a serial port.
        private static readonly Regex ReservedName = new(
            @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³]|CONIN\$|CONOUT\$)(\..*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool IsReservedName(string name) => ReservedName.IsMatch(name);
    }
}
