/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Hosting.Storage;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Archivar.Core.Accounts;
using WebCore.Caching;
using Archivar.Core.Email;
using Archivar.Core.Storage;
using Archivar.Web.Services;

namespace Archivar.Web.Accounts;

/// <summary>
/// Accounts, ported from TriviaNight: one identity per person (<see cref="User"/> + <see cref="UserProfile"/>), signing in
/// with a password or an emailed code, Member/Admin roles issued as role claims, and admin disable. See docs/accounts.md.
/// </summary>
public static class AccountServices
{
    public const string AdminPolicy = "Admin";

    public static WebApplicationBuilder AddAccounts(this WebApplicationBuilder builder, StorageBackend storage)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        // Storage: one JSON file per account/profile under App_Data (gitignored) unless Storage:* points elsewhere.
        // On App Service that folder is under the persistent %HOME% share. Both are wrapped in short-lived caches
        // (see docs/performance.md) because the cookie check reads a profile on every request. StorageCalls counts what gets
        // past the cache to real storage (the performance snapshots and the server panel on /admin/releases report it).
        services.AddSingleton<IUserRepository>(_ => new CachedUserRepository(StorageCalls.Wrap<IUserRepository>(new UserRepository(storage.Documents(AppStores.Users)))));
        services.AddSingleton<IUserProfileRepository>(_ => new CachedUserProfileRepository(StorageCalls.Wrap<IUserProfileRepository>(new UserProfileRepository(storage.Documents(AppStores.Profiles)))));

        // Email: Azure Communication Services when Email:AzureConnectionString and Email:FromEmail are both set;
        // otherwise a no-op sender that only logs - never a startup crash for a missing secret.
        var acsConnectionString = config["Email:AzureConnectionString"];
        var fromEmail = config["Email:FromEmail"];
        var emailConfigured = IsEmailConfigured(config);
        if (emailConfigured)
        {
            services.AddSingleton<IAzureEmailTransport>(_ => new AzureEmailTransport(acsConnectionString!));
            services.AddSingleton<IEmailSender>(sp => new AzureEmailSender(sp.GetRequiredService<IAzureEmailTransport>(), fromEmail!));
        }
        else
        {
            services.AddSingleton<IEmailSender, NullEmailSender>();
        }

        // Local development never sends codes for real by default (a dev machine may hold real secrets, and casual
        // testing must not email anyone): the code shows on screen instead. AccountEmail:SendInDevelopment=true sends for real.
        var isDevelopment = builder.Environment.IsDevelopment();
        var sendCodesForReal = emailConfigured && (!isDevelopment || config.GetValue<bool>("AccountEmail:SendInDevelopment"));
        services.AddSingleton(sp => new AccountEmailService(
            sp.GetRequiredService<IUserRepository>(),
            sp.GetRequiredService<IUserProfileRepository>(),
            sp.GetRequiredService<IEmailSender>(),
            deliveryConfigured: sendCodesForReal,
            exposeCodeForDevelopment: isDevelopment,
            onSendFailure: ex => sp.GetRequiredService<ILogger<AccountEmailService>>().LogWarning(ex, "Could not send a code email"),
            maxPerClientPerHour: config.GetValue("AccountEmail:MaxPerClientPerHour", AccountEmailService.DefaultMaxPerClientPerHour),
            maxPerHour: config.GetValue("AccountEmail:MaxPerHour", AccountEmailService.DefaultMaxPerHour),
            maxPerDay: config.GetValue("AccountEmail:MaxPerDay", AccountEmailService.DefaultMaxPerDay)));

        services.AddSingleton<AccountRateLimits>();

        var adminEmails = (config["Auth:AdminEmails"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "archivar_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/denied";

                // After every successful sign-in, whichever path produced it: make sure a profile exists, stop a
                // disabled account, and promote a configured admin email - only once that email is CONFIRMED, so
                // registering with an admin's address without proving it is never enough.
                options.Events.OnSignedIn = async context =>
                {
                    var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (string.IsNullOrWhiteSpace(userId))
                    {
                        return;
                    }

                    var profiles = context.HttpContext.RequestServices.GetRequiredService<IUserProfileRepository>();
                    var profile = await profiles.EnsureProfileAsync(userId, context.Principal?.Identity?.Name ?? "Member");
                    if (profile.IsDisabled)
                    {
                        // The sign-in endpoints refuse a disabled account before getting here; this is the backstop.
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
                    var emailConfirmed = context.Principal?.HasClaim(AccountClaims.EmailConfirmed, "1") == true;
                    if (!profile.IsAdmin && emailConfirmed && !string.IsNullOrWhiteSpace(email) && adminEmails.Contains(email.ToLowerInvariant()))
                    {
                        // The role claim catches up on the next request, in OnValidatePrincipal below.
                        await profiles.SetRoleAsync(userId, UserRole.Admin);
                    }
                };

                // Every request carrying the cookie: fail closed on a missing/disabled profile, and keep the role claim
                // in step with the stored role (the profile read is cached - see docs/performance.md), re-issuing the
                // cookie when it changed so a promotion or demotion applies without signing out.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    UserProfile? profile = null;
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        try
                        {
                            profile = await context.HttpContext.RequestServices.GetRequiredService<IUserProfileRepository>().GetAsync(userId);
                        }
                        catch (ArgumentException ex)
                        {
                            // An id that can't be a profile key is not a valid session.
                            SessionLogger(context).LogDebug(ex, "A session cookie carried an id that is not a valid profile key; rejecting it");
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Storage still unreadable after the repository's retries: fail closed (sign out), not a 500.
                            SessionLogger(context).LogWarning(ex, "The profile store could not be read while validating a session; signing the visitor out");
                        }
                    }

                    if (profile is null or { IsDisabled: true })
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    if (AccountClaims.RoleOf(context.Principal!) != profile.Role)
                    {
                        context.ReplacePrincipal(AccountClaims.WithRole(context.Principal!, profile.Role, CookieAuthenticationDefaults.AuthenticationScheme));
                        context.ShouldRenew = true;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));
        services.AddCascadingAuthenticationState();

        return builder;
    }

    /// <summary>True when real email delivery (Azure Communication Services) is configured. Shared with the alert-email logging sink.</summary>
    public static bool IsEmailConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Email:AzureConnectionString"]) && !string.IsNullOrWhiteSpace(config["Email:FromEmail"]);

    /// <summary>Signs the browser in as <paramref name="user"/> with its current role. Every sign-in path goes through here.</summary>
    public static async Task<ClaimsPrincipal> SignInUserAsync(HttpContext http, User user)
    {
        var profiles = http.RequestServices.GetRequiredService<IUserProfileRepository>();
        var role = (await profiles.GetAsync(user.Id))?.Role ?? UserRole.Member;
        var principal = AccountClaims.CreatePrincipal(user, role, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return principal;
    }

    private static ILogger SessionLogger(CookieValidatePrincipalContext context) =>
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Archivar.Accounts");
}
