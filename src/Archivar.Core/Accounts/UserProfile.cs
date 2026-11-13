/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json.Serialization;

namespace Archivar.Core.Accounts;

/// <summary>
/// The role/profile record for an account, separate from its login credentials (see <see cref="User"/>). One of
/// these is created/refreshed on every successful sign-in, so an admin can see and grant roles to anyone who has
/// ever signed in.
/// </summary>
public class UserProfile
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>Admin action: blocks sign-in and signs out any existing session once set - see the cookie auth
    /// <c>OnSignedIn</c>/<c>OnValidatePrincipal</c> events. Null <see cref="DisabledAt"/> means never disabled or
    /// currently re-enabled.</summary>
    public bool IsDisabled { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>An independent copy, so a cached profile is never mutated through a caller's reference.</summary>
    public UserProfile Clone() => (UserProfile)MemberwiseClone();

    [JsonIgnore]
    public bool IsAdmin => Role == UserRole.Admin;
}
