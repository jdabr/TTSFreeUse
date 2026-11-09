using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TTSFreeUse.Api
{
    /// <summary>
    /// TTSFreeUse is a web server on the user's own computer with no login, so
    /// it must not be usable by other websites open in the same browser.
    /// Together with "AllowedHosts" (which rejects DNS-rebinding requests
    /// addressed to other host names), this:
    /// <list type="bullet">
    /// <item>rejects API requests sent by other websites (cross-site requests),</item>
    /// <item>stops other sites from showing the page in a frame (clickjacking), and</item>
    /// <item>limits what the page itself may load (Content-Security-Policy).</item>
    /// </list>
    /// </summary>
    public static class LocalOnlyProtection
    {
        // The page uses inline script/styles, audio from the API (including
        // blob: URLs for instant playback), and nothing from other origins.
        private const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
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

                if (context.Request.Path.StartsWithSegments("/api") && IsFromAnotherSite(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Requests from other websites aren't allowed.");
                    return;
                }

                await next(context);
            });

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
