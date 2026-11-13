/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Archival;

namespace Archivar.Web.Archival;

/// <summary>What the candidates page shows: the library, how many servers were asked, and the shows none of them played.</summary>
public sealed record ArchivalReport(int ShowCount, IReadOnlyList<MediaServerKind> Servers, IReadOnlyList<Show> Candidates);

/// <summary>Gathers the library and every server's history and applies <see cref="NeverViewedFinder"/>. Orchestration only; the rule lives in Core.</summary>
public sealed class ArchivalReportService(ILibrarySource library, IEnumerable<IWatchHistorySource> servers)
{
    public async Task<ArchivalReport> BuildAsync(CancellationToken cancellationToken)
    {
        var shows = await library.GetShowsAsync(cancellationToken);
        var sources = servers.ToList();
        var plays = new List<PlayRecord>();
        foreach (var source in sources)
        {
            plays.AddRange(await source.GetPlaysAsync(cancellationToken));
        }

        return new ArchivalReport(shows.Count, sources.Select(s => s.Server).ToList(), NeverViewedFinder.Find(shows, plays));
    }
}
