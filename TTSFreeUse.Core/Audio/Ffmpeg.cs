using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using TTSFreeUse.Core.Downloads;

namespace TTSFreeUse.Core.Audio
{
    public class FfmpegOptions
    {
        /// <summary>Full path to an ffmpeg executable to use instead of searching.</summary>
        public string? Path { get; set; }

        /// <summary>Where a downloaded FFmpeg is kept; empty = per-user default folder.</summary>
        public string? ToolsDirectory { get; set; }

        /// <summary>Allow downloading FFmpeg automatically (Windows only).</summary>
        public bool AllowDownload { get; set; } = true;
    }

    /// <summary>A pinned FFmpeg build: where to get it and the SHA-256 it must match.</summary>
    public record FfmpegPackage(string Version, Uri Url, string Sha256)
    {
        /// <summary>
        /// gyan.dev "essentials" build for Windows x64, via its GitHub mirror
        /// (which keeps old versions, unlike gyan.dev itself). The hash matches
        /// the one gyan.dev publishes for this file.
        /// </summary>
        public static readonly FfmpegPackage WindowsDefault = new(
            "9.0.2",
            new Uri("https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip"),
            "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba");
    }

    /// <summary>
    /// Converts narration to compressed formats (MP3, M4B) with FFmpeg. Uses an
    /// FFmpeg already on the system if there is one; on Windows it can download
    /// a pinned, checksum-verified build on first use.
    /// </summary>
    public class Ffmpeg
    {
        // Speech is mono; 64 kbps sounds clean and is ~29 MB per hour.
        private const string Bitrate = "64k";

        private readonly FfmpegOptions _options;
        private readonly FfmpegPackage _package;
        private readonly HttpClient? _http;
        private readonly object _installLock = new();
        private Task<string>? _install;

        /// <summary>How long the download may go without receiving data (overridable for tests).</summary>
        internal TimeSpan StallTimeout { get; init; } = VerifiedDownload.DefaultStallTimeout;

        public Ffmpeg(FfmpegOptions options) : this(options, FfmpegPackage.WindowsDefault) { }

        public Ffmpeg(FfmpegOptions options, FfmpegPackage package, HttpClient? http = null)
        {
            _options = options;
            _package = package;
            _http = http;
        }

        private string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        public string ToolsDirectory => string.IsNullOrWhiteSpace(_options.ToolsDirectory)
            ? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.Create),
                "TTSFreeUse", "tools")
            : _options.ToolsDirectory;

        private string InstallDirectory => System.IO.Path.Combine(ToolsDirectory, $"ffmpeg-{_package.Version}");

        /// <summary>Whether this machine can get FFmpeg automatically when it's missing.</summary>
        public bool CanDownload => _options.AllowDownload && OperatingSystem.IsWindows();

        /// <summary>Status text for the UI while FFmpeg is being prepared.</summary>
        public string? Status { get; private set; }

        /// <summary>Finds an FFmpeg executable without downloading anything.</summary>
        public string? Locate()
        {
            if (!string.IsNullOrWhiteSpace(_options.Path))
                return File.Exists(_options.Path) ? _options.Path : null;

            var downloaded = System.IO.Path.Combine(InstallDirectory, ExecutableName);
            if (File.Exists(downloaded))
                return downloaded;

            return FindOnPath(Environment.GetEnvironmentVariable("PATH"), ExecutableName);
        }

        /// <summary>Looks for <paramref name="executable"/> in the folders listed in a PATH value.</summary>
        internal static string? FindOnPath(string? pathVariable, string executable)
        {
            foreach (var entry in (pathVariable ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = entry.Trim('"');
                // A relative entry (like ".") means "whatever folder is current",
                // which could be a Downloads folder holding a fake ffmpeg.exe.
                if (!System.IO.Path.IsPathFullyQualified(dir))
                    continue;
                var candidate = System.IO.Path.Combine(dir, executable);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>True if FFmpeg is installed or can be downloaded automatically.</summary>
        public bool IsObtainable => Locate() != null || CanDownload;

        public static string InstallInstructions =>
            "MP3 and M4B output need FFmpeg. Install it and restart TTSFreeUse — " +
            "macOS: \"brew install ffmpeg\"; Linux: \"sudo apt install ffmpeg\" (or your distribution's " +
            "package manager). Or choose WAV output, which needs nothing extra.";

        /// <summary>
        /// Returns the path to FFmpeg, downloading it first if needed and allowed.
        /// Concurrent callers share one download; <paramref name="cancellationToken"/>
        /// stops this caller waiting for it without stopping it for the others.
        /// </summary>
        public Task<string> EnsureAvailableAsync(CancellationToken cancellationToken = default)
        {
            var existing = Locate();
            if (existing != null)
                return Task.FromResult(existing);

            if (!CanDownload)
                return Task.FromException<string>(new InvalidOperationException(InstallInstructions));

            Task<string> install;
            lock (_installLock)
            {
                if (_install == null || _install.IsFaulted || _install.IsCanceled)
                    _install = Task.Run(DownloadAsync);
                install = _install;
            }
            return install.WaitAsync(cancellationToken);
        }

        private async Task<string> DownloadAsync()
        {
            Directory.CreateDirectory(ToolsDirectory);
            var archive = System.IO.Path.Combine(ToolsDirectory, $"ffmpeg-{_package.Version}.zip.partial");
            var staging = InstallDirectory + ".extracting";

            try
            {
                Status = "Downloading audio converter (first time only)…";
                await VerifiedDownload.DownloadAsync(_package.Url, archive, _package.Sha256, expectedBytes: null,
                    "The downloaded audio converter failed its integrity check and was discarded. Please try again.",
                    _http, StallTimeout,
                    (done, total) =>
                    {
                        if (total > 0)
                            Status = $"Downloading audio converter (first time only): {(double)done / total.Value:P0}";
                    },
                    CancellationToken.None);

                Status = "Installing audio converter…";
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);

                // Only the two files needed are extracted, by name, into a
                // fixed folder — archive paths are never used as destinations.
                using (var zip = ZipFile.OpenRead(archive))
                {
                    var exe = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("/bin/" + ExecutableName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("The audio converter download didn't contain ffmpeg.");
                    exe.ExtractToFile(System.IO.Path.Combine(staging, ExecutableName));

                    var license = zip.Entries.FirstOrDefault(e =>
                        e.FullName.Count(c => c == '/') == 1 && e.Name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase));
                    license?.ExtractToFile(System.IO.Path.Combine(staging, "LICENSE"));
                }

                if (Directory.Exists(InstallDirectory))
                    Directory.Delete(InstallDirectory, recursive: true);
                Directory.Move(staging, InstallDirectory);

                Status = null;
                return System.IO.Path.Combine(InstallDirectory, ExecutableName);
            }
            catch
            {
                Status = null;
                throw;
            }
            finally
            {
                try { File.Delete(archive); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (Directory.Exists(staging))
                    try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Command-line arguments to convert a WAV file. Exposed for testing.</summary>
        public static IReadOnlyList<string> BuildArguments(string inputWav, string output,
            AudioFormat format, AudioMetadata metadata)
        {
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                "-i", inputWav,
                "-map_metadata", "-1",
                "-ac", "1",
                "-b:a", Bitrate,
            };

            switch (format)
            {
                case AudioFormat.Mp3:
                    args.AddRange(new[] { "-c:a", "libmp3lame", "-id3v2_version", "3" });
                    break;
                case AudioFormat.M4b:
                    // "ipod" is FFmpeg's muxer for .m4a/.m4b audiobook files.
                    args.AddRange(new[] { "-c:a", "aac", "-movflags", "+faststart", "-f", "ipod" });
                    break;
                default:
                    throw new ArgumentException($"{format} doesn't need conversion.", nameof(format));
            }

            args.AddRange(new[] { "-metadata", $"title={metadata.Title}", "-metadata", "genre=Speech" });
            if (!string.IsNullOrWhiteSpace(metadata.Comment))
                args.AddRange(new[] { "-metadata", $"comment={metadata.Comment}" });

            args.Add(output);
            return args;
        }

        /// <summary>Converts a WAV file to <paramref name="format"/>.</summary>
        public async Task EncodeAsync(string inputWav, string output, AudioFormat format,
            AudioMetadata metadata, CancellationToken cancellationToken = default)
        {
            var exe = await EnsureAvailableAsync(cancellationToken);

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            // ArgumentList quotes each argument; nothing passes through a shell.
            foreach (var arg in BuildArguments(inputWav, output, format, metadata))
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Couldn't start the audio converter.");

            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            process.OutputDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Stop it, and let it close the half-written file before deleting it.
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.WaitForExit(TimeSpan.FromSeconds(5));
                DeleteQuietly(output);
                throw;
            }

            if (process.ExitCode != 0)
            {
                DeleteQuietly(output);
                string details;
                lock (stderr) details = stderr.ToString().Trim();
                throw new InvalidOperationException(
                    $"Audio conversion failed (FFmpeg exit code {process.ExitCode}). {Truncate(details, 500)}");
            }
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : "…" + s[^max..];

        private static void DeleteQuietly(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
