/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Security.Claims;

namespace Archivar.Core.Accounts;

/// <summary>The one place an account becomes a login: every sign-in path (password, emailed code, the dev
/// shortcut) builds its principal here, so every check elsewhere reads the same claims.</summary>
public static class AccountClaims
{
    /// <summary>Present (value "1") only when the account's email has been confirmed by a code.</summary>
    public const string EmailConfirmed = "archivar:email_confirmed";

    /// <summary>Present only while an Admin is masquerading as this account - holds the ORIGINAL admin's user id, so "Resume as
    /// yourself" can sign back in without a fresh login. Only <see cref="CreateMasqueradePrincipal"/> ever sets it.</summary>
    public const string MasqueradeOriginalAdminId = "archivar:masquerade_admin";

    /// <param name="role">The account's current role, issued as a <see cref="ClaimTypes.Role"/> claim. The cookie's
    /// <c>OnValidatePrincipal</c> re-issues the principal whenever the stored role changes, so a promotion or demotion
    /// takes effect without signing out.</param>
    public static ClaimsPrincipal CreatePrincipal(User user, UserRole role, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, role.ToString()),
        };
        if (user.EmailVerifiedAt is not null)
        {
            claims.Add(new Claim(EmailConfirmed, "1"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    /// <summary>The same claims <see cref="CreatePrincipal"/> builds for <paramref name="targetUser"/> (so every check elsewhere sees
    /// them exactly as they'd see themselves, role included), plus the claim that makes this a masquerade.</summary>
    public static ClaimsPrincipal CreateMasqueradePrincipal(User targetUser, UserRole targetRole, string originalAdminUserId, string authenticationType)
    {
        var principal = CreatePrincipal(targetUser, targetRole, authenticationType);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(MasqueradeOriginalAdminId, originalAdminUserId));
        return principal;
    }

    /// <summary>True while an Admin is viewing the site as someone else.</summary>
    public static bool IsMasquerading(ClaimsPrincipal principal) => principal.HasClaim(c => c.Type == MasqueradeOriginalAdminId);

    /// <summary>A copy of <paramref name="principal"/> whose role claim is exactly <paramref name="role"/> - every other
    /// claim kept as is.</summary>
    public static ClaimsPrincipal WithRole(ClaimsPrincipal principal, UserRole role, string authenticationType)
    {
        var claims = principal.Claims.Where(c => c.Type != ClaimTypes.Role).Append(new Claim(ClaimTypes.Role, role.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    /// <summary>The role claim, or null when the principal has none (or one this app doesn't know).</summary>
    public static UserRole? RoleOf(ClaimsPrincipal principal) =>
        Enum.TryParse<UserRole>(principal.FindFirst(ClaimTypes.Role)?.Value, out var role) ? role : null;
}
