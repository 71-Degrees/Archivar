/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Operations;

/// <summary>
/// Once the site has a public address (Site:BaseUrl), a page request that arrives on any other host name - the bare domain, the old
/// azurewebsites.net name - is sent to the same page on the public address, so shared links, bookmarks and printed QR codes all end up in one place.
/// </summary>
public static class CanonicalHostRedirect
{
    /// <summary>The address to redirect to, or null when the request should be served where it is.</summary>
    /// <param name="publicUrl">The configured public address (null/blank = feature off).</param>
    /// <param name="requestHost">The Host header, with or without a port.</param>
    /// <param name="method">The HTTP method; only GET and HEAD are redirected (a POST would lose its body).</param>
    /// <param name="path">The request path.</param>
    /// <param name="query">The query string including the leading '?', or empty.</param>
    /// <param name="isWebSocketUpgrade">Live connections are never redirected.</param>
    public static string? TargetFor(string? publicUrl, string? requestHost, string method, string path, string query, bool isWebSocketUpgrade)
    {
        if (string.IsNullOrWhiteSpace(publicUrl) || string.IsNullOrWhiteSpace(requestHost) || isWebSocketUpgrade
            || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var canonical))
        {
            return null;
        }

        if (!HttpMethods.IsGetOrHead(method))
        {
            return null;
        }

        // Health checks, the deploy tool and the version probe must answer on whatever name they are pointed at.
        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/version", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = StripDefaultPort(requestHost.Trim());
        var bare = host.Contains(':') ? host[..host.LastIndexOf(':')] : host;
        if (bare is "localhost" or "127.0.0.1" or "[::1]")
        {
            return null;
        }

        if (host.Equals(canonical.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return canonical.GetLeftPart(UriPartial.Authority) + (string.IsNullOrEmpty(path) ? "/" : path) + query;
    }

    private static string StripDefaultPort(string host) =>
        host.EndsWith(":443", StringComparison.Ordinal) ? host[..^4] : host.EndsWith(":80", StringComparison.Ordinal) ? host[..^3] : host;

    private static class HttpMethods
    {
        public static bool IsGetOrHead(string method) =>
            method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
    }
}
