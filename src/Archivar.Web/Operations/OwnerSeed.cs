/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Accounts;
using Archivar.Core.Storage;

namespace Archivar.Web.Operations;

/// <summary>
/// Development only: makes sure the owner's own account - PJJ, an Admin with a confirmed email - exists on every local
/// startup, so a fresh clone (or wiped App_Data) never needs a sign-up first. The same account is seeded in every
/// 71 Degrees .NET project (see dotnet-new-project-standards.md).
/// <para>
/// The password is never in the repo: the account gets the salted PBKDF2 hash stored in the Windows user environment
/// variable <c>DEV_SEED_PJJ_PASSWORD_HASH</c> (read from the user scope, so a shell started before it was set still sees
/// it). Without it the account has no password and signs in with an emailed code (shown on screen in Development) or
/// <c>/dev/seed?as=pjj</c>. An existing account keeps its own password; only its role and email confirmation are ensured.
/// </para>
/// <para>Off when <c>DevSeed:Enabled=false</c> (the integration tests turn it off so they start from empty storage).</para>
/// </summary>
public static class OwnerSeed
{
    public const string Email = "patrickjohnson859@hotmail.com";
    public const string DisplayName = "PJJ";
    public const string PasswordHashVariable = "DEV_SEED_PJJ_PASSWORD_HASH";

    public static async Task SeedOwnerAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue("DevSeed:Enabled", true))
        {
            return;
        }

        try
        {
            var owner = await EnsureAsync(app.Services.GetRequiredService<IUserRepository>(), app.Services.GetRequiredService<IUserProfileRepository>());
            app.Logger.LogInformation("Dev seed: {DisplayName} ({Email}) is an Admin{Password}", owner.DisplayName, owner.Email,
                string.IsNullOrEmpty(owner.PasswordHash) ? " - no password (sign in with a code)" : string.Empty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A seeding hiccup must never stop the dev server from starting.
            app.Logger.LogWarning(ex, "Dev seed of the owner account failed");
        }
    }

    public static Task<User> EnsureAsync(IUserRepository users, IUserProfileRepository profiles) =>
        EnsureSeedAccountAsync(users, profiles, Email, DisplayName, UserRole.Admin, StoredPasswordHash());

    /// <summary>
    /// The one way a Development seed account is made sure of (PJJ here, the sample accounts in <see cref="DevEndpoints"/>):
    /// created if missing, email confirmed, given <paramref name="passwordHash"/> only when it has no password of its own,
    /// then its profile set to <paramref name="role"/> and re-enabled. Safe to run on every startup.
    /// </summary>
    public static async Task<User> EnsureSeedAccountAsync(IUserRepository users, IUserProfileRepository profiles, string email, string name, UserRole role, string? passwordHash = null)
    {
        var user = await users.GetByEmailAsync(email);
        if (user is null)
        {
            user = new User { Email = email, DisplayName = name, EmailVerifiedAt = DateTimeOffset.UtcNow, PasswordHash = passwordHash ?? string.Empty };
            await users.CreateAsync(user);
        }
        else if (user.EmailVerifiedAt is null || (string.IsNullOrEmpty(user.PasswordHash) && passwordHash is not null))
        {
            user.EmailVerifiedAt ??= DateTimeOffset.UtcNow;
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                user.PasswordHash = passwordHash ?? string.Empty;
            }

            await users.SaveAsync(user);
        }

        await profiles.EnsureProfileAsync(user.Id, user.DisplayName);
        await profiles.SetRoleAsync(user.Id, role);
        await profiles.SetDisabledAsync(user.Id, false);
        return user;
    }

    /// <summary>The hash from the user environment, or null when it isn't set or doesn't look like one of ours.</summary>
    private static string? StoredPasswordHash()
    {
        var hash = Environment.GetEnvironmentVariable(PasswordHashVariable, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(PasswordHashVariable);
        return hash is not null && hash.Split('.').Length == 3 ? hash.Trim() : null;
    }
}
