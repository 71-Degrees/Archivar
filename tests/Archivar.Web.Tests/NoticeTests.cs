/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Archivar.Web.Tests;

public partial class NoticeTests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string tokenPage, string action, Dictionary<string, string> fields)
    {
        var match = TokenPattern().Match(await browser.GetStringAsync(tokenPage));
        fields["__RequestVerificationToken"] = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task AdminSetsANotice_EveryPageShowsTheBanner_UntilCleared()
    {
        var admin = NewBrowser();
        await admin.GetAsync("/dev/seed?as=admin");
        var startsAt = DateTimeOffset.UtcNow.AddHours(2).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

        var set = await PostFormAsync(admin, "/admin/notice", "/admin/notice/set", new()
        {
            ["startsAt"] = startsAt,
            ["minutes"] = "45",
            ["message"] = "Upgrading <things>",
            ["email"] = "everyone",
            ["preview"] = "true",
        });

        // Preview: /dev/seed ensures seed.member, seed.admin and the owner (PJJ), all confirmed; nothing sent.
        Assert.Equal("/admin/notice?done=set&recipients=3&sent=0&failed=0&dry=1", set.Headers.Location!.OriginalString);
        var guestPage = await NewBrowser().GetStringAsync("/terms");
        Assert.Contains("Scheduled downtime", guestPage);
        Assert.Contains("for about 45 minutes", guestPage);
        Assert.Contains("Upgrading &lt;things&gt;", guestPage);

        var cleared = await PostFormAsync(admin, "/admin/notice", "/admin/notice/clear", []);
        Assert.Equal("/admin/notice?done=cleared", cleared.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Scheduled downtime", await NewBrowser().GetStringAsync("/terms"));
    }

    [Fact]
    public async Task AWindowThatHasAlreadyEnded_IsRefused()
    {
        var admin = NewBrowser();
        await admin.GetAsync("/dev/seed?as=admin");

        var response = await PostFormAsync(admin, "/admin/notice", "/admin/notice/set",
            new() { ["startsAt"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), ["minutes"] = "30" });

        Assert.Equal("/admin/notice?error=past", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Members_CannotReachTheNoticePage()
    {
        var member = NewBrowser();
        await member.GetAsync("/dev/seed?as=member");

        var response = await member.GetAsync("/admin/notice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/denied", response.Headers.Location!.ToString());
    }
}
