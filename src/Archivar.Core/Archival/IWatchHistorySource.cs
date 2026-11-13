/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>Play history from one media server (Plex, Jellyfin or Emby).</summary>
public interface IWatchHistorySource
{
    MediaServerKind Server { get; }

    Task<IReadOnlyList<PlayRecord>> GetPlaysAsync(CancellationToken cancellationToken);
}
