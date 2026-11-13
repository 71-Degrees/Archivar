/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>Where the list of shows comes from (Sonarr). Lidarr and Radarr get their own sources later.</summary>
public interface ILibrarySource
{
    Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken);
}
