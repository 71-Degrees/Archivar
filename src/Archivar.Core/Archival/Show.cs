/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>A TV show as the library manager (Sonarr) knows it. <paramref name="TvdbId"/> is the join key to watch history.</summary>
public sealed record Show(int TvdbId, string Title, long SizeBytes, DateTimeOffset? Added, string? Path);
