/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Security.Claims;
using Archivar.Core.Accounts;
using Archivar.Core.Feedback;
using Archivar.Core.Storage;
using Archivar.Web.Accounts;
using WebCore.Accounts;
using WebCore.Hosting.Accounts;
using WebCore.Hosting.Security;

namespace Archivar.Web.Feedback;

/// <summary>
/// Plain form endpoints for the feedback button, the contact page, "Your data" and the admin inbox - static forms, no
/// interactive circuit and no script, so they work under the strict CSP and without JavaScript.
/// </summary>
public static class FeedbackEndpoints
{
    private const string LogCategory = "Archivar.Feedback";

    public static void MapFeedbackEndpoints(this WebApplication app)
    {
        var forms = app.MapFormGroup();

        forms.MapPost("/feedback", async (HttpContext http, FeedbackService feedback) =>
        {
            var form = await http.Request.ReadFormAsync();
            var fromContact = form["source"].ToString() == "contact";
            var page = LocalReturnUrl.Sanitize(form["page"].ToString());
            var submission = new FeedbackSubmission
            {
                Message = form["message"].ToString(),
                PageUrl = page,
                Source = fromContact ? "contact" : "widget",
                IsContentReport = !fromContact && form["report"].ToString() == "true",
            };

            if (http.User.Identity?.IsAuthenticated == true)
            {
                submission.IsGuest = false;
                submission.SubmitterName = http.User.Identity.Name;
                submission.SubmitterId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            var callerKey = submission.SubmitterId ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var status = await feedback.SubmitAsync(submission, callerKey);
            var result = status == FeedbackSubmitStatus.Accepted ? "sent" : status.ToString().ToLowerInvariant();
            return fromContact
                ? Results.Redirect("/contact?result=" + result)
                : Results.Redirect($"/feedback/thanks?result={result}&returnUrl={Uri.EscapeDataString(page)}");
        });

        // "Your data": delete every feedback submission tied to the signed-in account.
        forms.MapPost("/account/data/delete", async (HttpContext http, IFeedbackRepository repository, ILoggerFactory loggers) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var deleted = await repository.DeleteForSubmitterAsync(userId);
            loggers.CreateLogger(LogCategory).LogInformation("User {UserId} deleted their data: {Count} feedback submission(s)", userId, deleted);
            return Results.Redirect("/account/data?deleted=" + deleted);
        }).RequireAuthorization();

        forms.MapPost("/admin/feedback/resolve", async (HttpContext http, IFeedbackRepository repository) =>
        {
            var form = await http.Request.ReadFormAsync();
            var resolved = await repository.ResolveAsync(form["id"].ToString());
            return Results.Redirect("/admin/feedback" + (resolved ? "?done=1" : "?error=unknown"));
        }).RequireAuthorization(AccountPolicies.Admin);
    }
}
