using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TTSFreeUse.Api;
using TTSFreeUse.Api.Controllers;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Jobs;
using TTSFreeUse.Core.TTS;
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
        var model = new KokoroModel(_dir.Path)
        {
            ExpectedSha256 = Convert.ToHexString(SHA256.HashData(archive)),
            ExpectedBytes = archive.Length,
        };

        using var http = new HttpClient(new FixedResponseHandler(archive));
        await model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(model.IsInstalled());
    }

    [Fact]
    public async Task RejectsADownloadThatDoesntMatchTheChecksum()
    {
        var archive = FakeModelArchive();
        var model = new KokoroModel(_dir.Path) { ExpectedBytes = archive.Length }; // the real model's checksum

        using var http = new HttpClient(new FixedResponseHandler(archive));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("wasn't the expected version", ex.Message);
        Assert.False(model.IsInstalled());
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path)); // no leftovers
    }

    [Fact]
    public async Task RejectsAFileOfTheWrongSizeWithoutDownloadingIt()
    {
        // e.g. a Wi-Fi sign-in page served in place of the model.
        var handler = new FixedResponseHandler("<html>Sign in to continue</html>"u8.ToArray());
        var model = new KokoroModel(_dir.Path); // expects the real model's size

        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(model.IsInstalled());
    }

    [Fact]
    public async Task GivesUpOnADownloadThatStopsSendingData()
    {
        var model = new KokoroModel(_dir.Path) { ExpectedBytes = null, StallTimeout = TimeSpan.FromMilliseconds(300) };

        using var http = new HttpClient(new StallingHandler());
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            model.EnsureInstalledAsync(http: http, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        Assert.Contains("no data arrived", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path)); // the partial file is removed
    }
}

/// <summary>A server that sends the start of a file and then goes silent without hanging up.</summary>
internal sealed class StallingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

    private sealed class StallingStream : Stream
    {
        private bool _sentFirstPart;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (!_sentFirstPart)
            {
                _sentFirstPart = true;
                buffer.Span[0] = 42;
                return 1;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class LocalOnlyProtectionTests
{
    private static ConnectionInfo Connection(string? remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress == null ? null : IPAddress.Parse(remoteAddress);
        return context.Connection;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData(null)] // not a network connection
    public void AllowsConnectionsFromThisComputer(string? address) =>
        Assert.False(LocalOnlyProtection.IsFromAnotherComputer(Connection(address)));

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.5")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.20")]
    public void RefusesConnectionsFromOtherComputers(string address) =>
        Assert.True(LocalOnlyProtection.IsFromAnotherComputer(Connection(address)));

    /// <summary>Sends a request through the protection to an endpoint that answers 200.</summary>
    private static async Task<HttpContext> Send(string path, string? remoteAddress = "127.0.0.1", string? fetchSite = null)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseLocalOnlyProtection();
        app.Run(c => { c.Response.StatusCode = 200; return Task.CompletedTask; });

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Host = new HostString("localhost:5138");
        context.Connection.RemoteIpAddress = remoteAddress == null ? null : IPAddress.Parse(remoteAddress);
        if (fetchSite != null)
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        return context;
    }

    [Fact]
    public async Task BlocksOtherComputersAndOtherSitesButNotTheAppsOwnPage()
    {
        Assert.Equal(200, (await Send("/api/library", fetchSite: "same-origin")).Response.StatusCode);
        Assert.Equal(200, (await Send("/index.html", fetchSite: "cross-site")).Response.StatusCode); // just the page
        Assert.Equal(403, (await Send("/api/library", fetchSite: "cross-site")).Response.StatusCode);
        Assert.Equal(403, (await Send("/index.html", remoteAddress: "192.168.1.20")).Response.StatusCode);
    }

    [Fact]
    public async Task ThePageMayNotRunInlineScripts()
    {
        var csp = (await Send("/")).Response.Headers.ContentSecurityPolicy.ToString();

        Assert.Contains("script-src 'self';", csp);
        Assert.DoesNotContain("unsafe", csp.Split(';').Single(d => d.Trim().StartsWith("script-src")));
        Assert.Contains("frame-ancestors 'none'", csp);
    }

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

/// <summary>
/// Damaged or deliberately malformed documents must fail with a message, never
/// take the app down. A stack overflow can't be caught: it ends the process.
/// </summary>
public class MaliciousDocumentTests
{
    private static readonly DocumentReader Reader = new(new ITextExtractor[]
    {
        new PdfExtractor(), new DocxExtractor(), new PlainTextExtractor(),
    });

    private static MemoryStream NestedEquation(int depth) => DocxExtractorTests.BuildDocxXml(
        "<w:p><m:oMath>" + string.Concat(Enumerable.Repeat("<m:d><m:e>", depth)) +
        "<m:r><m:t>x</m:t></m:r>" + string.Concat(Enumerable.Repeat("</m:e></m:d>", depth)) + "</m:oMath></w:p>");

    [Fact]
    public async Task RefusesAWordDocumentNestedDeeperThanAnyRealOne()
    {
        // 1,000 levels of brackets in an equation was enough to crash the app.
        var ex = await Assert.ThrowsAsync<DocumentReadException>(() =>
            Reader.ReadAsync(NestedEquation(5_000), "nested.docx"));

        Assert.Contains("nested too deeply", ex.Message);
    }

    [Fact]
    public async Task StillReadsDeepButRealisticEquations()
    {
        Assert.Equal("x", (await Reader.ReadAsync(NestedEquation(100), "brackets.docx")).Trim());
    }

    /// <summary>A one-page PDF whose page list is nested <paramref name="depth"/> levels deep.</summary>
    private static MemoryStream NestedPageTree(int depth)
    {
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>" };
        for (int i = 0; i < depth; i++)
            objects.Add($"<< /Type /Pages /Kids [{i + 3} 0 R] /Count 1 {(i > 0 ? $"/Parent {i + 1} 0 R" : "")} >>");
        objects.Add($"<< /Type /Page /Parent {depth + 1} 0 R /MediaBox [0 0 612 792] >>");

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return new MemoryStream(Encoding.Latin1.GetBytes(pdf.ToString()));
    }

    [Fact]
    public async Task SurvivesAPdfNestedDeeperThanANormalStackAllows()
    {
        // Measured: about 5,000 levels overflowed the stack inside the PDF library.
        var text = await Reader.ReadAsync(NestedPageTree(20_000), "nested.pdf");

        Assert.Equal("", text.Trim()); // the single page is blank
    }
}

public class RequestValidationTests
{
    private sealed class ReadyEngine : ITtsEngine
    {
        public string Name => "kokoro";
        public int SampleRate => 24000;
        public IReadOnlyList<TtsVoice> Voices { get; } = new[] { new TtsVoice("af_heart", "Heart", "en-US", "female") };
        public string DefaultVoice => "af_heart";
        public bool IsReady => true;
        public string Status => "Ready.";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<float[]> SynthesizeAsync(string text, TtsOptions options, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData(float.NaN)] // forms and JSON strings accept "NaN"; it crashed the voice engine
    [InlineData(float.PositiveInfinity)]
    [InlineData(0.49f)]
    [InlineData(2.01f)]
    public void RejectsSpeedsTheVoiceCantUse(float speed) =>
        Assert.IsType<BadRequestObjectResult>(RequestValidation.Check(new ReadyEngine(), null, speed));

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void AcceptsNormalSpeeds(float speed) =>
        Assert.Null(RequestValidation.Check(new ReadyEngine(), "af_heart", speed));

    [Theory]
    [InlineData("am_adam")]                                // a Kokoro voice that isn't offered
    [InlineData("en-US-JennyNeural\"><audio src=\"x\"/>")] // markup instead of a name
    [InlineData("")]
    public void RejectsUnknownVoices(string voice) =>
        Assert.IsType<BadRequestObjectResult>(RequestValidation.Check(new ReadyEngine(), voice, 1.0f));
}

public class LibraryControllerTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ExplainsWhenAFileOpenElsewhereCantBeDeleted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete files that are open.");
        var path = Path.Combine(_dir.Path, "chapter.mp3");
        File.WriteAllText(path, "x");
        var controller = new LibraryController(new AudioLibrary(_dir.Path));

        IActionResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // like a music player
            result = controller.Delete("chapter.mp3");

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("open in another program", (string)conflict.Value!);
        Assert.True(File.Exists(path));
    }
}

public class EngineWarmupTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(4, 240)]
    [InlineData(5, 300)]
    [InlineData(10_000, 300)]
    public void RetriesSlowDownToEveryFiveMinutes(int failures, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), EngineWarmupService.RetryDelay(failures));
}
