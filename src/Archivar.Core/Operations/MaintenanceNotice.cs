/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Operations;

/// <summary>
/// A scheduled-downtime announcement: shown as a banner on every page of the site from the moment it is set until
/// <see cref="ExpiresAt"/>, and (optionally) emailed to the admins or every confirmed account.
/// </summary>
public class MaintenanceNotice
{
    /// <summary>When the downtime is expected to begin.</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>When it is expected to be over; the banner disappears then. Blank means 30 minutes after <see cref="StartsAt"/>.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>What the people reading it need to know, in plain words.</summary>
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The moment after which the notice no longer matters.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset ExpiresAt => EndsAt ?? StartsAt.AddMinutes(30);

    public bool IsActiveAt(DateTimeOffset now) => now < ExpiresAt;
}
