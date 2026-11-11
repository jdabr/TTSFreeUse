using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TTSFreeUse.Api
{
    /// <summary>
    /// TTSFreeUse is a web server on the user's own computer with no login, so
    /// nothing but the user's own browser and tools should be able to use it.
    /// Together with "AllowedHosts" (which rejects DNS-rebinding requests
    /// addressed to other host names), this:
    /// <list type="bullet">
    /// <item>refuses connections from other computers, in case the server is
    /// ever set up to listen on the network,</item>
    /// <item>rejects API requests sent by other websites (cross-site requests),</item>
    /// <item>stops other sites from showing the page in a frame (clickjacking), and</item>
    /// <item>limits what the page itself may load and run (Content-Security-Policy).</item>
    /// </list>
    /// </summary>
    public static class LocalOnlyProtection
    {
        // Scripts only from TTSFreeUse's own files (never inline), audio from
        // the API (including blob: URLs for instant playback), nothing from
        // other origins. Inline styles are allowed; they can't run code.
        private const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; media-src 'self' blob:; connect-src 'self'; " +
            "frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

        public static IApplicationBuilder UseLocalOnlyProtection(this IApplicationBuilder app) =>
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";

                if (IsFromAnotherComputer(context.Connection))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("TTSFreeUse only accepts connections from this computer.");
                    return;
                }

                if (context.Request.Path.StartsWithSegments("/api") && IsFromAnotherSite(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Requests from other websites aren't allowed.");
                    return;
                }

                await next(context);
            });

        /// <summary>
        /// True when the connection comes over the network from another device.
        /// TTSFreeUse listens only on this computer (localhost), but a changed
        /// setting such as "--urls http://0.0.0.0:5138" would otherwise let
        /// anyone on the same Wi-Fi use it, since it has no login.
        /// </summary>
        internal static bool IsFromAnotherComputer(ConnectionInfo connection)
        {
            var remote = connection.RemoteIpAddress;
            if (remote == null)
                return false; // not a network connection (e.g. in-process)
            if (remote.IsIPv4MappedToIPv6)
                remote = remote.MapToIPv4();
            return !IPAddress.IsLoopback(remote);
        }

        /// <summary>
        /// True when a browser says the request came from a different website.
        /// Requests from TTSFreeUse's own page, and from non-browser tools such
        /// as curl or scripts (which send neither header), are allowed.
        /// </summary>
        internal static bool IsFromAnotherSite(HttpRequest request)
        {
            // Sent by all current browsers: "same-origin", "same-site",
            // "cross-site", or "none" (typed into the address bar).
            var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
            if (fetchSite is "cross-site" or "same-site")
                return true;
            if (fetchSite is "same-origin" or "none")
                return false;

            // Older browsers: compare the Origin header with the address the
            // request was sent to.
            var origin = request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin))
                return false;
            return !Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                   || !string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
