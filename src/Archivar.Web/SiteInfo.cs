/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Reflection;

namespace Archivar.Web;

/// <summary>Facts about the running site: its public address (Site:BaseUrl, optional) and which build this is.</summary>
public sealed class SiteInfo
{
    public SiteInfo(string? configuredBaseUrl)
    {
        PublicUrl = string.IsNullOrWhiteSpace(configuredBaseUrl) ? null : configuredBaseUrl.TrimEnd('/');

        // The SDK stamps "1.0.0+<full commit hash>" from git at build time. Shortened to "1.0.0+<first 8>": short enough for a
        // banner, long enough to identify a build - and the part after '+' is what the release-notes page links to.
        var stamp = typeof(SiteInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var plus = stamp.IndexOf('+');
        Version = plus >= 0 && stamp.Length - plus - 1 > 8 ? stamp[..(plus + 9)] : stamp;
    }

    public string Version { get; }

    /// <summary>The commit this build was made from (the part of <see cref="Version"/> after '+'), or null.</summary>
    public string? CommitSha => Version.Contains('+') ? Version[(Version.IndexOf('+') + 1)..] : null;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>The configured public address (no trailing slash), or null when none is set. Setting it also turns on the
    /// canonical-host redirect.</summary>
    public string? PublicUrl { get; }

    /// <summary>The configured public address, or the one this request came in on when none is set. No trailing slash.</summary>
    public string BaseUrlFor(HttpContext http) => PublicUrl ?? $"{http.Request.Scheme}://{http.Request.Host}";
}
