/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Archivar.Core.Accounts;
using Archivar.Core.Feedback;
using Archivar.Core.Storage;
using WebCore.Accounts;
using WebCore.Hosting.Accounts;

namespace Archivar.Web.Operations;

/// <summary>
/// Development-only shortcuts (ported from TriviaNight's /dev/*). Mapped only when the environment is Development, so they
/// don't exist anywhere else. Plain GETs so they can be typed into the address bar.
/// <list type="bullet">
/// <item><c>/dev/logged-out[?to=/path]</c> - signs this browser out and stops <c>Auth:DevAutoLoginEmail</c> signing it back in.</item>
/// <item><c>/dev/logged-in[?to=/path]</c> - lets the auto-login sign this browser in again.</item>
/// <item><c>/dev/seed[?as=member|admin|pjj][&amp;to=/path]</c> - makes sure the seed accounts and sample feedback exist, then signs in as one
/// (pjj = the owner's own account, see <see cref="OwnerSeed"/>).</item>
/// </list>
/// </summary>
public static class DevEndpoints
{
    public const string NoAutoLoginCookie = "archivar_dev_no_autologin";
    public const string SeedMemberEmail = "seed.member@example.test";
    public const string SeedAdminEmail = "seed.admin@example.test";

    public static void MapDevEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapGet("/dev/logged-out", async (HttpContext http, string? to) =>
        {
            http.Response.Cookies.Append(NoAutoLoginCookie, "1", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect(LocalReturnUrl.Sanitize(to));
        });

        app.MapGet("/dev/logged-in", async (HttpContext http, string? to) =>
        {
            http.Response.Cookies.Delete(NoAutoLoginCookie);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect(LocalReturnUrl.Sanitize(to));
        });

        app.MapGet("/dev/seed", async (HttpContext http, string? @as, string? to, IUserRepository users, IUserProfileRepository profiles, IFeedbackRepository feedback) =>
        {
            var member = await OwnerSeed.EnsureSeedAccountAsync(users, profiles, SeedMemberEmail, "Seed Member", Roles.Member);
            var admin = await OwnerSeed.EnsureSeedAccountAsync(users, profiles, SeedAdminEmail, "Seed Admin", Roles.Admin);

            // Fixed ids, so seeding again overwrites instead of piling up copies.
            await feedback.SaveAsync(new FeedbackSubmission
            {
                Id = "seedfeedback0000000000000001",
                Message = "Sample feedback from the seed member.",
                PageUrl = "/",
                IsGuest = false,
                SubmitterId = member.Id,
                SubmitterName = member.DisplayName,
            });
            await feedback.SaveAsync(new FeedbackSubmission
            {
                Id = "seedfeedback0000000000000002",
                Message = "Sample content report from a guest.",
                PageUrl = "/terms",
                IsContentReport = true,
            });

            http.Response.Cookies.Append(NoAutoLoginCookie, "1", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
            var owner = await OwnerSeed.EnsureAsync(users, profiles);
            var target = @as?.ToLowerInvariant() switch { "admin" => admin, "pjj" => owner, _ => member };
            await http.RequestServices.GetRequiredService<AccountSignIn>().SignInAsync(http, target);
            return Results.LocalRedirect(LocalReturnUrl.Sanitize(to ?? (target == member ? "/account" : "/admin/users")));
        });
    }
}
