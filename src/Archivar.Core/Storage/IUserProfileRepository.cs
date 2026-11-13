/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Accounts;

namespace Archivar.Core.Storage;

public interface IUserProfileRepository
{
    Task<UserProfile?> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Creates a profile (defaulting to <see cref="UserRole.Member"/>) if one doesn't already exist for this
    /// user ID, or refreshes the display name if it does - called on every successful sign-in, so a profile always
    /// exists for anyone who's ever signed in. Never changes an existing profile's role.</summary>
    Task<UserProfile> EnsureProfileAsync(string userId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Admin action: sets a user's role. No-ops if the user has no profile yet (they need to sign in at
    /// least once first).</summary>
    Task SetRoleAsync(string userId, UserRole role, CancellationToken cancellationToken = default);

    /// <summary>Admin action: disables or re-enables a user's account, stamping/clearing
    /// <see cref="UserProfile.DisabledAt"/> to match. No-ops if the user has no profile yet.</summary>
    Task SetDisabledAsync(string userId, bool disabled, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserProfile>> ListAllAsync(CancellationToken cancellationToken = default);
}
