/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Archival;

namespace Archivar.Core.Tests.Archival;

public class NeverViewedFinderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    private static Show ShowOf(int id, string title, long size = 1) => new(id, title, size, Now, null);

    [Fact]
    public void A_show_played_on_any_server_is_not_a_candidate()
    {
        var shows = new[] { ShowOf(1, "Seen on Plex"), ShowOf(2, "Seen on Emby"), ShowOf(3, "Never seen") };
        var plays = new[]
        {
            new PlayRecord(MediaServerKind.Plex, 1, "Seen on Plex", Now),
            new PlayRecord(MediaServerKind.Emby, 2, "Seen on Emby", Now),
        };

        var result = NeverViewedFinder.Find(shows, plays);

        Assert.Equal(["Never seen"], result.Select(s => s.Title));
    }

    [Fact]
    public void A_play_without_a_tvdb_id_matches_by_title_ignoring_case_and_punctuation()
    {
        var shows = new[] { ShowOf(10, "Marvel's Agents of S.H.I.E.L.D.") };
        var plays = new[] { new PlayRecord(MediaServerKind.Jellyfin, null, "marvels agents of shield", Now) };

        Assert.Empty(NeverViewedFinder.Find(shows, plays));
    }

    [Fact]
    public void Candidates_are_listed_largest_first()
    {
        var shows = new[] { ShowOf(1, "Small", 10), ShowOf(2, "Large", 500), ShowOf(3, "Medium", 100) };

        var result = NeverViewedFinder.Find(shows, []);

        Assert.Equal(["Large", "Medium", "Small"], result.Select(s => s.Title));
    }
}
