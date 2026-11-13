/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Archival;

namespace Archivar.Web.Archival;

/// <summary>
/// Stand-in library until Sonarr is configured ("unconfigured = safely off"): fixed sample shows so the page and the
/// rule can be seen working. Replaced by the real Sonarr source in the next slice; never mixed with real data.
/// </summary>
public sealed class SampleLibrarySource(TimeProvider time) : ILibrarySource
{
    private const long Gb = 1024L * 1024 * 1024;

    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        IReadOnlyList<Show> shows =
        [
            new(1001, "Sample: Never Watched Drama", 84 * Gb, now.AddDays(-400), @"D:\Shows\Never Watched Drama"),
            new(1002, "Sample: Watched Comedy", 40 * Gb, now.AddDays(-300), @"D:\Shows\Watched Comedy"),
            new(1003, "Sample: Forgotten Documentary", 12 * Gb, now.AddDays(-700), @"D:\Shows\Forgotten Documentary"),
        ];
        return Task.FromResult(shows);
    }
}

/// <summary>Stand-in watch history: one sample show played on Plex, so the other two are flagged.</summary>
public sealed class SampleWatchHistorySource(TimeProvider time) : IWatchHistorySource
{
    public MediaServerKind Server => MediaServerKind.Plex;

    public Task<IReadOnlyList<PlayRecord>> GetPlaysAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlayRecord> plays =
            [new(MediaServerKind.Plex, 1002, "Sample: Watched Comedy", time.GetUtcNow().AddDays(-30))];
        return Task.FromResult(plays);
    }
}
