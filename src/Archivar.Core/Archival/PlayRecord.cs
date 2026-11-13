/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>One play of one episode on one media server. A show with no records on any server has never been viewed.</summary>
public sealed record PlayRecord(MediaServerKind Server, int? TvdbId, string ShowTitle, DateTimeOffset PlayedAt);
