/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Accounts;

namespace Archivar.Core.Accounts;

/// <summary>
/// A user's permission level, independent of how they signed in (password or emailed code) - tracked against their
/// durable user ID. Every new account is a <see cref="Member"/>; an <see cref="Admin"/> manages accounts and roles.
/// Signed in, it becomes a <see cref="System.Security.Claims.ClaimTypes.Role"/> claim (see <see cref="WebCore.Accounts.AccountClaims"/>),
/// so checks use <c>[Authorize(Roles = Roles.Admin)]</c> / <c>AuthorizeView Roles</c> rather than reading the profile.
/// </summary>
public enum UserRole
{
    Member = 0,
    Admin = 1,
}

/// <summary>Role names as they appear in claims, for attribute and <c>AuthorizeView</c> checks (which need constants).</summary>
public static class Roles
{
    public const string Member = nameof(UserRole.Member);
    public const string Admin = nameof(UserRole.Admin);

    /// <summary>The role list for the shared accounts module: stored and issued as these names (profiles saved before 2026-11-16 hold the enum number, read by index).</summary>
    public static readonly AccountRoles All = AccountRoles.FromEnum(UserRole.Member, UserRole.Admin);
}
