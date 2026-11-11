using System.Security.Cryptography;

namespace TTSFreeUse.Core.Downloads
{
    /// <summary>
    /// Downloads a file that must match a pinned SHA-256, used for the voice
    /// model and the audio converter. A file that doesn't match is reported,
    /// never used; a connection that stops sending data is given up on instead
    /// of waiting forever.
    /// </summary>
    internal static class VerifiedDownload
    {
        /// <summary>How long a download may go without receiving any data.</summary>
        public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Saves <paramref name="url"/> to <paramref name="path"/>. Throws
        /// <see cref="InvalidDataException"/> with <paramref name="mismatchMessage"/>
        /// when the file isn't the expected one (wrong size or checksum), and
        /// <see cref="TimeoutException"/> when the connection stalls. The caller
        /// deletes <paramref name="path"/> afterwards if this throws.
        /// </summary>
        /// <param name="expectedBytes">Exact size, if known: a server offering a
        /// different file (or a Wi-Fi login page) is caught before downloading it.</param>
        /// <param name="progress">Called with (bytes so far, total bytes if known).</param>
        public static async Task DownloadAsync(Uri url, string path, string expectedSha256, long? expectedBytes,
            string mismatchMessage, HttpClient? http, TimeSpan stallTimeout, Action<long, long?>? progress,
            CancellationToken cancellationToken)
        {
            var ownsClient = http == null;
            // No limit on the total time: a large download on a slow connection
            // is fine as long as data keeps arriving (see stallTimeout).
            http ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            try
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stall.CancelAfter(stallTimeout);
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? expectedBytes;
                if (expectedBytes != null && total != expectedBytes)
                    throw new InvalidDataException(mismatchMessage);

                await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                var buffer = new byte[81920];
                long done = 0;
                while (true)
                {
                    stall.CancelAfter(stallTimeout); // restart the clock for every read
                    var read = await source.ReadAsync(buffer, stall.Token);
                    if (read == 0)
                        break;
                    done += read;
                    if (done > expectedBytes)
                        throw new InvalidDataException(mismatchMessage);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    progress?.Invoke(done, total);
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(mismatchMessage);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The download stopped: no data arrived for {stallTimeout.TotalSeconds:0} seconds.");
            }
            finally
            {
                if (ownsClient)
                    http.Dispose();
            }
        }
    }
}
