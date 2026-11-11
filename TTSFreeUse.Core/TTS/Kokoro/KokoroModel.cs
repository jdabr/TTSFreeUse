using System.Formats.Tar;
using ICSharpCode.SharpZipLib.BZip2;
using TTSFreeUse.Core.Downloads;

namespace TTSFreeUse.Core.TTS.Kokoro
{
    /// <summary>
    /// Locates the Kokoro voice model on disk and downloads it on first run.
    /// </summary>
    public class KokoroModel
    {
        // The full-precision model, not the int8 one: int8 is ~2.5x smaller to
        // download but measured ~4x slower on a Ryzen 5 7500X3D (1.1x vs 4.8x
        // real time), which matters far more for book-length text.
        public const string ModelName = "kokoro-multi-lang-v1_0";

        public static readonly Uri DownloadUrl = new(
            $"https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/{ModelName}.tar.bz2");

        /// <summary>
        /// SHA-256 of the archive, as published by GitHub for this release file.
        /// A download that doesn't match is discarded, so a tampered or
        /// corrupted model is never installed. If the sherpa-onnx project
        /// replaces the file, this must be updated in a new TTSFreeUse release.
        /// </summary>
        public const string DownloadSha256 = "60703707bc05ccd48d60e1eb723f38b07d6e474795a0b6a588d7220400e0f7a4";

        /// <summary>Archive size. A server offering a different size is offering a different file.</summary>
        public const long DownloadBytes = 349_886_476;

        // Files the engine needs; if any is missing the model is (re)downloaded.
        private static readonly string[] RequiredFiles =
            { "model.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt", "espeak-ng-data" };

        /// <summary>Folder that holds downloaded models (the model lives in a subfolder).</summary>
        public string ModelsRoot { get; }

        public string Directory => Path.Combine(ModelsRoot, ModelName);

        public string ModelFile => Path.Combine(Directory, "model.onnx");
        public string VoicesFile => Path.Combine(Directory, "voices.bin");
        public string TokensFile => Path.Combine(Directory, "tokens.txt");
        public string LexiconFile => Path.Combine(Directory, "lexicon-us-en.txt");
        public string EspeakDataDir => Path.Combine(Directory, "espeak-ng-data");

        // What a download must match, and how long it may stall (overridable for tests).
        internal string ExpectedSha256 { get; init; } = DownloadSha256;
        internal long? ExpectedBytes { get; init; } = DownloadBytes;
        internal TimeSpan StallTimeout { get; init; } = VerifiedDownload.DefaultStallTimeout;

        public KokoroModel(string? modelsRoot = null)
        {
            ModelsRoot = string.IsNullOrWhiteSpace(modelsRoot) ? DefaultModelsRoot() : modelsRoot;
        }

        /// <summary>
        /// Per-user data folder: %LOCALAPPDATA%\TTSFreeUse\models on Windows,
        /// ~/.local/share/TTSFreeUse/models on Linux/macOS.
        /// </summary>
        public static string DefaultModelsRoot() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            "TTSFreeUse", "models");

        public bool IsInstalled() => RequiredFiles.All(f =>
            File.Exists(Path.Combine(Directory, f)) || System.IO.Directory.Exists(Path.Combine(Directory, f)));

        /// <summary>
        /// Downloads and extracts the model if it isn't installed yet. Safe to
        /// interrupt: partial downloads never replace a complete model.
        /// </summary>
        /// <param name="progress">Download progress from 0 to 1.</param>
        public async Task EnsureInstalledAsync(IProgress<double>? progress = null,
            HttpClient? http = null, CancellationToken cancellationToken = default)
        {
            if (IsInstalled())
                return;

            System.IO.Directory.CreateDirectory(ModelsRoot);
            var archive = Path.Combine(ModelsRoot, ModelName + ".tar.bz2.partial");
            var staging = Path.Combine(ModelsRoot, ModelName + ".extracting");

            try
            {
                await VerifiedDownload.DownloadAsync(DownloadUrl, archive, ExpectedSha256, ExpectedBytes,
                    "The downloaded voice model wasn't the expected version and was discarded. TTSFreeUse " +
                    "will try again; if this keeps happening, check for a newer version of TTSFreeUse.",
                    http, StallTimeout, (done, total) => progress?.Report(Math.Min(1.0, (double)done / (total ?? DownloadBytes))),
                    cancellationToken);

                if (System.IO.Directory.Exists(staging))
                    System.IO.Directory.Delete(staging, recursive: true);
                System.IO.Directory.CreateDirectory(staging);

                await using (var file = File.OpenRead(archive))
                await using (var bz2 = new BZip2InputStream(file))
                {
                    // TarFile rejects entries that would escape the target folder.
                    await TarFile.ExtractToDirectoryAsync(bz2, staging, overwriteFiles: true, cancellationToken);
                }

                // The archive contains a top-level folder named after the model.
                var extracted = Path.Combine(staging, ModelName);
                if (!System.IO.Directory.Exists(extracted))
                    throw new InvalidDataException($"Downloaded archive did not contain '{ModelName}'.");

                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
                System.IO.Directory.Move(extracted, Directory);

                if (!IsInstalled())
                    throw new InvalidDataException("Downloaded voice model is incomplete.");
            }
            finally
            {
                TryDelete(archive);
                if (System.IO.Directory.Exists(staging))
                    try { System.IO.Directory.Delete(staging, recursive: true); } catch (IOException) { }
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
