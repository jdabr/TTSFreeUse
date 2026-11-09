using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.AspNetCore.Http;
using TTSFreeUse.Api;
using TTSFreeUse.Core.TTS.Kokoro;

namespace TTSFreeUse.Core.Tests;

public class KokoroModelDownloadTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    /// <summary>A tiny stand-in for the real model archive, with the files the engine checks for.</summary>
    private static byte[] FakeModelArchive()
    {
        var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            var root = KokoroModel.ModelName + "/";
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, root));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, root + "espeak-ng-data/"));
            foreach (var name in new[] { "model.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt" })
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, root + name)
                    { DataStream = new MemoryStream("x"u8.ToArray()) });
        }

        var bz2 = new MemoryStream();
        using (var compressor = new BZip2OutputStream(bz2) { IsStreamOwner = false })
            compressor.Write(tar.ToArray());
        return bz2.ToArray();
    }

    [Fact]
    public async Task InstallsADownloadThatMatchesTheChecksum()
    {
        var archive = FakeModelArchive();
        var model = new KokoroModel(_dir.Path) { ExpectedSha256 = Convert.ToHexString(SHA256.HashData(archive)) };

        using var http = new HttpClient(new FixedResponseHandler(archive));
        await model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(model.IsInstalled());
    }

    [Fact]
    public async Task RejectsADownloadThatDoesntMatchTheChecksum()
    {
        var model = new KokoroModel(_dir.Path); // expects the real model's checksum

        using var http = new HttpClient(new FixedResponseHandler(FakeModelArchive()));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("didn't match", ex.Message);
        Assert.False(model.IsInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path)); // no leftovers
    }
}

public class LocalOnlyProtectionTests
{
    private static HttpRequest Request(string? fetchSite = null, string? origin = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost:5138");
        if (fetchSite != null)
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        if (origin != null)
            context.Request.Headers.Origin = origin;
        return context.Request;
    }

    [Theory]
    [InlineData("same-origin", null)]                      // TTSFreeUse's own page
    [InlineData("none", null)]                             // typed into the address bar
    [InlineData(null, null)]                               // curl, scripts
    [InlineData(null, "http://localhost:5138")]            // older browser, own page
    public void AllowsTheAppsOwnPageAndLocalTools(string? fetchSite, string? origin) =>
        Assert.False(LocalOnlyProtection.IsFromAnotherSite(Request(fetchSite, origin)));

    [Theory]
    [InlineData("cross-site", null)]                       // any other website
    [InlineData("same-site", null)]                        // another app on localhost
    [InlineData(null, "https://evil.example")]             // older browser, other website
    [InlineData(null, "http://localhost:3000")]            // older browser, other local port
    [InlineData(null, "null")]                             // sandboxed frame or file://
    public void RefusesRequestsFromOtherSites(string? fetchSite, string? origin) =>
        Assert.True(LocalOnlyProtection.IsFromAnotherSite(Request(fetchSite, origin)));
}
