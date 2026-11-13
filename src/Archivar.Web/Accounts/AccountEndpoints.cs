/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Archivar.Core.Accounts;
using Archivar.Core.Storage;

namespace Archivar.Web.Accounts;

/// <summary>
/// The account form endpoints. Plain endpoints rather than Blazor components because signing in has to write the auth
/// cookie. Each one validates the antiforgery token, re-checks everything server-side, and redirects back to a page
/// with a short status in the query string. Paths differ from the pages' own (/account/login vs /account/login-submit)
/// because a page and a minimal-API endpoint on the same POST path are ambiguous.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Binds an emailed sign-in code to the browser that asked for it.</summary>
    public const string CodeClientCookie = "archivar_code_client";

    private const string LogCategory = "Archivar.Accounts";

    /// <summary>A real hash of a random value, so a sign-in attempt with no account behind it costs as much as a wrong password.</summary>
    private static readonly Lazy<string> TimingDummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString("N")));

    public static void MapAccountEndpoints(this WebApplication app)
    {
        // Every form endpoint is on a form group: antiforgery is validated there, before the handler (see FormAntiforgery).
        var forms = app.MapFormGroup();

        forms.MapPost("/account/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/");
        });

        forms.MapPost("/account/register-submit", async (HttpContext http, IUserRepository users, AccountRateLimits limits, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var rawEmail = form["email"].ToString();
            var displayName = DisplayNames.Tidy(form["displayName"].ToString());
            var password = form["password"].ToString();
            var returnUrl = LocalReturnUrl.Sanitize(form["returnUrl"].ToString());

            IResult Fail(string reason) => Results.Redirect(
                $"/account/register?error={Uri.EscapeDataString(reason)}&returnUrl={Uri.EscapeDataString(returnUrl)}" +
                $"&email={Uri.EscapeDataString(rawEmail.Trim())}&displayName={Uri.EscapeDataString(displayName)}");

            var email = AccountEmailService.NormalizeEmail(rawEmail);
            if (email is null)
            {
                return Fail("Please enter a valid email address.");
            }

            if (!DisplayNames.IsValid(displayName))
            {
                return Fail($"Please enter a display name of up to {DisplayNames.MaxLength} characters.");
            }

            if (password.Length is < 8 or > 256)
            {
                return Fail("Password must be at least 8 characters.");
            }

            if (password != form["confirmPassword"].ToString())
            {
                return Fail("Passwords don't match.");
            }

            if (!limits.TryAcquire(http))
            {
                return Fail("Too many attempts right now - please try again in a few minutes.");
            }

            var user = new User { Email = email, DisplayName = displayName, PasswordHash = PasswordHasher.Hash(password) };
            try
            {
                await users.CreateAsync(user);
            }
            catch (InvalidOperationException)
            {
                return Fail("That email is already registered - try signing in instead.");
            }

            loggers.CreateLogger(LogCategory).LogInformation("New account registered: {UserId}", user.Id);
            await AccountServices.SignInUserAsync(http, user);
            return Results.LocalRedirect(returnUrl);
        });

        forms.MapPost("/account/login-submit", async (HttpContext http, IUserRepository users, IUserProfileRepository profiles, AccountRateLimits limits) =>
        {
            var form = await http.Request.ReadFormAsync();
            var rawEmail = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var returnUrl = LocalReturnUrl.Sanitize(form["returnUrl"].ToString());
            var common = $"&returnUrl={Uri.EscapeDataString(returnUrl)}&email={Uri.EscapeDataString(rawEmail)}";

            if (!limits.TryAcquire(http))
            {
                return Results.Redirect("/account/login?error=ratelimited" + common);
            }

            var email = AccountEmailService.NormalizeEmail(rawEmail);
            var user = email is null ? null : await users.GetByEmailAsync(email);
            // The same answer for "no such account", "no password on this account" and "wrong password" - telling
            // them apart would let this endpoint probe which emails are registered. That includes the time taken: with
            // no hash to check, check the dummy one, so every attempt pays the same PBKDF2 cost.
            var hasPassword = !string.IsNullOrEmpty(user?.PasswordHash);
            var passwordMatches = PasswordHasher.Verify(password, hasPassword ? user!.PasswordHash : TimingDummyHash.Value);
            if (!hasPassword || !passwordMatches)
            {
                return Results.Redirect("/account/login?error=1" + common);
            }

            // Only reachable with the right password, so naming the real reason leaks nothing.
            if ((await profiles.GetAsync(user!.Id)) is { IsDisabled: true })
            {
                return Results.Redirect("/account/login?error=disabled" + common);
            }

            await AccountServices.SignInUserAsync(http, user);
            return Results.LocalRedirect(returnUrl);
        });

        forms.MapPost("/account/code-send", async (HttpContext http, AccountEmailService accountEmail) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var returnUrl = LocalReturnUrl.Sanitize(form["returnUrl"].ToString());

            var clientKey = ClientKey(http);
            var result = await accountEmail.RequestSignInCodeAsync(email, clientKey);
            var common = $"&email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return result.Status == EmailRequestStatus.Sent
                ? Results.Redirect("/account/login?step=code" + common + (result.DevCode is null ? string.Empty : "&devCode=" + result.DevCode))
                : Results.Redirect("/account/login?codeError=" + result.Status.ToString().ToLowerInvariant() + common);
        });

        forms.MapPost("/account/code-verify", async (HttpContext http, AccountEmailService accountEmail, IUserProfileRepository profiles) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var returnUrl = LocalReturnUrl.Sanitize(form["returnUrl"].ToString());
            var common = $"&email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(returnUrl)}";

            var clientKey = http.Request.Cookies[CodeClientCookie];
            if (string.IsNullOrWhiteSpace(clientKey))
            {
                return Results.Redirect("/account/login?codeError=expired" + common);
            }

            var result = await accountEmail.ConfirmSignInAsync(email, clientKey, form["code"].ToString(), form["displayName"].ToString());
            switch (result.Status)
            {
                case CodeCheckStatus.Confirmed:
                    if ((await profiles.GetAsync(result.User!.Id)) is { IsDisabled: true })
                    {
                        return Results.Redirect("/account/login?error=disabled" + common);
                    }

                    await AccountServices.SignInUserAsync(http, result.User!);
                    http.Response.Cookies.Delete(CodeClientCookie);
                    return Results.LocalRedirect(returnUrl);
                case CodeCheckStatus.UsePassword:
                    return Results.Redirect("/account/login?error=usepassword" + common);
                default:
                    // A wrong code keeps the code box open; an expired or over-tried one goes back to asking for a new code.
                    var keepOpen = result.Status == CodeCheckStatus.WrongCode ? "&step=code" : string.Empty;
                    return Results.Redirect("/account/login?codeError=" + result.Status.ToString().ToLowerInvariant() + keepOpen + common);
            }
        });

        // Signed in: send a code to confirm the current email, or to a new address to change it.
        forms.MapPost("/account/email-send", async (HttpContext http, AccountEmailService accountEmail) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var result = await accountEmail.RequestEmailConfirmationAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier), email);
            var common = "&email=" + Uri.EscapeDataString(email);
            return result.Status == EmailRequestStatus.Sent
                ? Results.Redirect("/account?step=code" + common + (result.DevCode is null ? string.Empty : "&devCode=" + result.DevCode))
                : Results.Redirect("/account?emailError=" + result.Status.ToString().ToLowerInvariant() + common);
        }).RequireAuthorization();

        // Confirming re-issues the cookie, so the email-confirmed claim (and a changed address) apply at once - and a
        // configured admin email is promoted by OnSignedIn the moment it is proven.
        forms.MapPost("/account/email-verify", async (HttpContext http, AccountEmailService accountEmail) =>
        {
            var form = await http.Request.ReadFormAsync();
            var common = "&email=" + Uri.EscapeDataString(form["email"].ToString().Trim());
            var result = await accountEmail.ConfirmEmailAsync(http.User.FindFirstValue(ClaimTypes.NameIdentifier), form["code"].ToString());
            if (result.Status != CodeCheckStatus.Confirmed)
            {
                var keepOpen = result.Status == CodeCheckStatus.WrongCode ? "&step=code" : string.Empty;
                return Results.Redirect("/account?emailError=" + result.Status.ToString().ToLowerInvariant() + keepOpen + common);
            }

            await AccountServices.SignInUserAsync(http, result.User!);
            return Results.Redirect("/account?confirmed=1");
        }).RequireAuthorization();

        // Forgot password: a code to the address, then the code and a new password. Neither signs anyone in.
        forms.MapPost("/account/reset-send", async (HttpContext http, AccountEmailService accountEmail) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var result = await accountEmail.RequestPasswordResetAsync(email, ClientKey(http));
            var common = $"&email={Uri.EscapeDataString(email)}";
            return result.Status == EmailRequestStatus.Sent
                ? Results.Redirect("/account/forgot?step=code" + common + (result.DevCode is null ? string.Empty : "&devCode=" + result.DevCode))
                : Results.Redirect("/account/forgot?codeError=" + result.Status.ToString().ToLowerInvariant() + common);
        });

        forms.MapPost("/account/reset-submit", async (HttpContext http, AccountEmailService accountEmail, AccountRateLimits limits, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var common = $"&email={Uri.EscapeDataString(email)}";

            // The password is checked first, so a typo doesn't spend one of the code's five tries.
            if (password.Length is < 8 or > 256)
            {
                return Results.Redirect("/account/forgot?step=code&passwordError=short" + common);
            }

            if (password != form["confirmPassword"].ToString())
            {
                return Results.Redirect("/account/forgot?step=code&passwordError=mismatch" + common);
            }

            if (!limits.TryAcquire(http))
            {
                return Results.Redirect("/account/forgot?step=code&passwordError=ratelimited" + common);
            }

            var result = await accountEmail.ResetPasswordAsync(email, ClientKey(http), form["code"].ToString(), password);
            if (result.Status != CodeCheckStatus.Confirmed)
            {
                var keepOpen = result.Status == CodeCheckStatus.WrongCode ? "&step=code" : string.Empty;
                return Results.Redirect("/account/forgot?codeError=" + result.Status.ToString().ToLowerInvariant() + keepOpen + common);
            }

            loggers.CreateLogger(LogCategory).LogInformation("Password reset by emailed code: {UserId}", result.User!.Id);
            return Results.Redirect("/account/login?reset=1" + common);
        });

        // Change password (signed in): the current one is required when the account has one; an account that has none can set one.
        forms.MapPost("/account/password-change", async (HttpContext http, IUserRepository users, AccountRateLimits limits, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var current = form["currentPassword"].ToString();
            var password = form["password"].ToString();
            if (password.Length is < 8 or > 256)
            {
                return Results.Redirect("/account?passwordError=short");
            }

            if (password != form["confirmPassword"].ToString())
            {
                return Results.Redirect("/account?passwordError=mismatch");
            }

            if (!limits.TryAcquire(http))
            {
                return Results.Redirect("/account?passwordError=ratelimited");
            }

            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = userId is null ? null : await users.GetByIdAsync(userId);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            if (!string.IsNullOrEmpty(user.PasswordHash) && !PasswordHasher.Verify(current, user.PasswordHash))
            {
                return Results.Redirect("/account?passwordError=wrong");
            }

            user.PasswordHash = PasswordHasher.Hash(password);
            await users.SaveAsync(user);
            loggers.CreateLogger(LogCategory).LogInformation("Password changed: {UserId}", user.Id);
            return Results.Redirect("/account?passwordDone=1");
        }).RequireAuthorization();

        // Resume: open to whoever is signed in, because while masquerading the principal is the target's (often a Member).
        forms.MapPost("/account/masquerade-stop", async (HttpContext http, IUserRepository users, ILoggerFactory loggers) =>
        {
            var originalAdminId = http.User.FindFirstValue(AccountClaims.MasqueradeOriginalAdminId);
            if (originalAdminId is null)
            {
                return Results.LocalRedirect("/");
            }

            var admin = await users.GetByIdAsync(originalAdminId);
            if (admin is null)
            {
                // The admin account is gone - sign out rather than stay signed in as the person being viewed.
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/");
            }

            await AccountServices.SignInUserAsync(http, admin);
            loggers.CreateLogger(LogCategory).LogInformation("Admin {AdminId} resumed their own identity", originalAdminId);
            return Results.LocalRedirect("/admin/users");
        }).RequireAuthorization();

        MapAdminEndpoints(app);
    }

    private static void MapAdminEndpoints(WebApplication app)
    {
        var admin = app.MapFormGroup("/admin/users").RequireAuthorization(AccountServices.AdminPolicy);

        admin.MapPost("/role", async (HttpContext http, IUserProfileRepository profiles, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var target = form["userId"].ToString();
            if (!Enum.TryParse<UserRole>(form["role"].ToString(), out var role) || !Enum.IsDefined(role))
            {
                return Results.Redirect("/admin/users?error=badrole");
            }

            // An admin can't change their own role - the only way to lose the last admin by accident.
            if (IsSelf(http, target))
            {
                return Results.Redirect("/admin/users?error=self");
            }

            if (await TryGetProfileAsync(profiles, target) is null)
            {
                return Results.Redirect("/admin/users?error=unknown");
            }

            await profiles.SetRoleAsync(target, role);
            loggers.CreateLogger(LogCategory).LogInformation("Admin {AdminId} set {UserId} to {Role}", http.User.FindFirstValue(ClaimTypes.NameIdentifier), target, role);
            return Results.Redirect("/admin/users?done=role");
        });

        // Masquerade: an Admin takes on another account's identity to see the site exactly as they do. The role and ownership checks
        // read the claims (role re-issued from the profile store each request), so signing in as the target is enough on its own.
        admin.MapPost("/masquerade", async (HttpContext http, IUserRepository users, IUserProfileRepository profiles, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var targetId = form["userId"].ToString();
            var adminId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId is null || await TryGetProfileAsync(profiles, adminId) is not { Role: UserRole.Admin })
            {
                return Results.Forbid();
            }

            // Resume first - stacking one masquerade on another would lose track of who the real admin is.
            if (AccountClaims.IsMasquerading(http.User))
            {
                return Results.Redirect("/admin/users?error=already");
            }

            if (IsSelf(http, targetId))
            {
                return Results.Redirect("/admin/users?error=self");
            }

            var targetProfile = await TryGetProfileAsync(profiles, targetId);
            var target = targetProfile is null ? null : await users.GetByIdAsync(targetId);
            if (targetProfile is null || target is null)
            {
                return Results.Redirect("/admin/users?error=unknown");
            }

            if (targetProfile.IsDisabled)
            {
                return Results.Redirect("/admin/users?error=disabled");
            }

            var principal = AccountClaims.CreateMasqueradePrincipal(target, targetProfile.Role, adminId, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Warning, not Information: loud enough to reach the alert digest - every masquerade should leave a visible trail.
            loggers.CreateLogger(LogCategory).LogWarning("Admin {AdminId} started masquerading as {TargetId} ({TargetEmail})", adminId, targetId, target.Email);
            return Results.LocalRedirect("/wave");
        });

        admin.MapPost("/disabled", async (HttpContext http, IUserProfileRepository profiles, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            var target = form["userId"].ToString();
            var disable = form["disabled"].ToString() == "true";
            if (IsSelf(http, target))
            {
                return Results.Redirect("/admin/users?error=self");
            }

            if (await TryGetProfileAsync(profiles, target) is null)
            {
                return Results.Redirect("/admin/users?error=unknown");
            }

            await profiles.SetDisabledAsync(target, disable);
            loggers.CreateLogger(LogCategory).LogInformation("Admin {AdminId} {Action} {UserId}", http.User.FindFirstValue(ClaimTypes.NameIdentifier), disable ? "disabled" : "re-enabled", target);
            return Results.Redirect("/admin/users?done=" + (disable ? "disabled" : "enabled"));
        });
    }

    /// <summary>The cookie that binds an emailed code to the browser that asked for it (set on first use).</summary>
    private static string ClientKey(HttpContext http)
    {
        var clientKey = http.Request.Cookies[CodeClientCookie];
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            clientKey = Guid.NewGuid().ToString("N");
            http.Response.Cookies.Append(CodeClientCookie, clientKey, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                MaxAge = TimeSpan.FromHours(1),
            });
        }

        return clientKey;
    }

    private static bool IsSelf(HttpContext http, string userId) =>
        string.Equals(http.User.FindFirstValue(ClaimTypes.NameIdentifier), userId, StringComparison.Ordinal);

    /// <summary>The profile, or null for an unknown or malformed id (never an exception from a tampered form).</summary>
    private static async Task<UserProfile?> TryGetProfileAsync(IUserProfileRepository profiles, string userId)
    {
        try
        {
            return string.IsNullOrWhiteSpace(userId) ? null : await profiles.GetAsync(userId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
