/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Hosting.Storage;
using System.Globalization;
using System.Security.Claims;
using WebCore.Accounts;
using WebCore.Caching;
using WebCore.Email;
using WebCore.Hosting.Accounts;
using WebCore.Hosting.Security;
using Archivar.Core.Operations;
using Archivar.Core.Storage;
using Archivar.Web.Accounts;

namespace Archivar.Web.Operations;

/// <summary>
/// Scheduled-downtime notices (ported from TriviaNight): an admin sets one on <c>/admin/notice</c>, every page shows it as a
/// banner until it expires, and it can be emailed to the admins or to every confirmed account. TriviaNight set it from its
/// deploy tool through an API key; Archivar uses the admin page until the deploy tool grows that command.
/// </summary>
public static class MaintenanceEndpoints
{
    private const string LogCategory = "Archivar.Maintenance";

    public static WebApplicationBuilder AddMaintenance(this WebApplicationBuilder builder, StorageBackend storage)
    {
        // Development never emails the announcement unless Maintenance:SendInDevelopment=true - same rule as sign-in codes.
        var sendEmails = AccountServices.IsEmailConfigured(builder.Configuration)
            && (!builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Maintenance:SendInDevelopment"));
        builder.Services.AddSingleton<IMaintenanceStore>(_ => StorageCalls.Wrap<IMaintenanceStore>(new MaintenanceStore(storage.Documents(AppStores.SiteSettings))));
        builder.Services.AddSingleton(sp => new MaintenanceService(
            sp.GetRequiredService<IMaintenanceStore>(),
            sp.GetRequiredService<IUserProfileRepository>(),
            sp.GetRequiredService<IUserRepository>(),
            sp.GetRequiredService<IEmailSender>(),
            sendEmails,
            logger: sp.GetService<ILogger<MaintenanceService>>()));
        return builder;
    }

    public static void MapMaintenanceEndpoints(this WebApplication app)
    {
        var admin = app.MapFormGroup("/admin/notice").RequireAuthorization(AccountPolicies.Admin);

        admin.MapPost("/set", async (HttpContext http, MaintenanceService maintenance, ILoggerFactory loggers) =>
        {
            var form = await http.Request.ReadFormAsync();
            // <input type="datetime-local"> sends no time zone; the page labels the field UTC and it is read as UTC.
            if (!DateTime.TryParse(form["startsAt"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var starts))
            {
                return Results.Redirect("/admin/notice?error=time");
            }

            var minutes = int.TryParse(form["minutes"].ToString(), out var m) ? Math.Clamp(m, 5, 24 * 60) : 30;
            var message = form["message"].ToString().Trim();
            if (message.Length > 500)
            {
                return Results.Redirect("/admin/notice?error=long");
            }

            var startsAt = new DateTimeOffset(starts, TimeSpan.Zero);
            var notice = new MaintenanceNotice { StartsAt = startsAt, EndsAt = startsAt.AddMinutes(minutes), Message = message };
            if (!notice.IsActiveAt(DateTimeOffset.UtcNow))
            {
                return Results.Redirect("/admin/notice?error=past");
            }

            await maintenance.SetAsync(notice);
            var logger = loggers.CreateLogger(LogCategory);
            logger.LogInformation("Admin {AdminId} set a downtime notice for {StartsAt:u}", http.User.FindFirstValue(ClaimTypes.NameIdentifier), startsAt);

            var email = form["email"].ToString();
            if (email is not ("admins" or "everyone"))
            {
                return Results.Redirect("/admin/notice?done=set");
            }

            var audience = email == "everyone" ? MaintenanceAudience.Everyone : MaintenanceAudience.Admins;
            var preview = form["preview"].ToString() == "true";
            var result = await maintenance.NotifyAsync(notice, audience, dryRun: preview);
            logger.LogInformation("Downtime email to {Audience}: {Recipients} recipient(s), {Sent} sent, {Failed} failed, dry run {DryRun}",
                audience, result.Recipients.Count, result.Sent, result.Failed, result.DryRun);
            return Results.Redirect($"/admin/notice?done=set&recipients={result.Recipients.Count}&sent={result.Sent}&failed={result.Failed}&dry={(result.DryRun ? 1 : 0)}");
        });

        admin.MapPost("/clear", async (HttpContext http, MaintenanceService maintenance) =>
        {
            await maintenance.ClearAsync();
            return Results.Redirect("/admin/notice?done=cleared");
        });
    }
}
