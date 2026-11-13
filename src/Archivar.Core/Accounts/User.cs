/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Accounts;

/// <summary>
/// One person's account - the single identity behind logging in and roles
/// (see <see cref="UserProfile"/>). It can sign in with a password, with a code emailed to <see cref="Email"/>, or both
/// (an account created by code alone has no password). <see cref="EmailVerifiedAt"/> records when the email was
/// proven, which is what the email-confirmed claim reflects.
/// </summary>
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Empty for an account created by emailed code alone; such an account can never sign in by password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>When the email was proven by an emailed code; null until then.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>An independent copy, so a cached account is never mutated through a caller's reference.</summary>
    public User Clone() => (User)MemberwiseClone();
}
