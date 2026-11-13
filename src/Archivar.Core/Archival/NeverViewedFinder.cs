/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>
/// The one rule Archivar exists for: a show no media server has ever played is a candidate for archival.
/// Matches on TVDB id; a play without one falls back to the normalised title, since servers differ in what they expose.
/// </summary>
public static class NeverViewedFinder
{
    public static IReadOnlyList<Show> Find(IEnumerable<Show> shows, IEnumerable<PlayRecord> plays)
    {
        var playedIds = new HashSet<int>();
        var playedTitles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var play in plays)
        {
            if (play.TvdbId is { } id)
            {
                playedIds.Add(id);
            }
            playedTitles.Add(NormaliseTitle(play.ShowTitle));
        }

        return shows
            .Where(s => !playedIds.Contains(s.TvdbId) && !playedTitles.Contains(NormaliseTitle(s.Title)))
            .OrderByDescending(s => s.SizeBytes)
            .ToList();
    }

    private static string NormaliseTitle(string title) =>
        new string(title.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
