/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Archival;

/// <summary>The media servers whose watch history decides whether a show has ever been viewed.</summary>
public enum MediaServerKind
{
    Plex,
    Jellyfin,
    Emby,
}
