/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Archivar.Core.Storage;

namespace Archivar.Web.Tests;

public partial class FeedbackFlowTests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("devCode=(\\d{6})")]
    private static partial Regex DevCodePattern();

    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string tokenPage, string action, Dictionary<string, string> fields)
    {
        var match = TokenPattern().Match(await browser.GetStringAsync(tokenPage));
        fields["__RequestVerificationToken"] = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpClient> SignInAsync(HttpClient browser, string email)
    {
        var sent = await PostFormAsync(browser, "/account/login", "/account/code-send", new() { ["email"] = email });
        var code = DevCodePattern().Match(sent.Headers.Location!.OriginalString).Groups[1].Value;
        await PostFormAsync(browser, "/account/login", "/account/code-verify", new() { ["email"] = email, ["code"] = code });
        return browser;
    }

    [Theory]
    [InlineData("/terms", "Terms of Service")]
    [InlineData("/privacy", "Privacy Policy")]
    [InlineData("/contact", "Contact us")]
    public async Task PublicPages_Render_WithTheFooterAndFeedbackButton(string path, string heading)
    {
        var html = await NewBrowser().GetStringAsync(path);

        Assert.Contains(heading, html);
        Assert.Contains("href=\"privacy\"", html);
        Assert.Contains("action=\"feedback\"", html);
    }

    [Fact]
    public async Task Sitemap_ListsTheLegalAndContactPages()
    {
        var sitemap = await NewBrowser().GetStringAsync("/sitemap.xml");

        Assert.Contains("/terms</loc>", sitemap);
        Assert.Contains("/privacy</loc>", sitemap);
        Assert.Contains("/contact</loc>", sitemap);
    }

    [Fact]
    public async Task GuestWidgetFeedback_IsSaved_AndReturnsToThePage()
    {
        var browser = NewBrowser();
        var response = await PostFormAsync(browser, "/terms", "/feedback",
            new() { ["message"] = "guest widget note", ["page"] = "/terms", ["report"] = "true" });

        Assert.Equal("/feedback/thanks?result=sent&returnUrl=%2Fterms", response.Headers.Location!.OriginalString);
        var saved = (await factory.Services.GetRequiredService<IFeedbackRepository>().ListAllAsync()).Single(f => f.Message == "guest widget note");
        Assert.True(saved.IsGuest);
        Assert.True(saved.IsContentReport);
        Assert.Equal("widget", saved.Source);
        Assert.Equal("/terms", saved.PageUrl);
    }

    [Fact]
    public async Task ContactForm_IsSaved_NeverAsAContentReport()
    {
        var response = await PostFormAsync(NewBrowser(), "/contact", "/feedback",
            new() { ["message"] = "contact form note", ["source"] = "contact", ["page"] = "/contact", ["report"] = "true" });

        Assert.Equal("/contact?result=sent", response.Headers.Location!.OriginalString);
        var saved = (await factory.Services.GetRequiredService<IFeedbackRepository>().ListAllAsync()).Single(f => f.Message == "contact form note");
        Assert.Equal("contact", saved.Source);
        Assert.False(saved.IsContentReport);
    }

    [Fact]
    public async Task EmptyFeedback_IsRefused_AndAnOffSitePageIsNeverAReturnUrl()
    {
        var response = await PostFormAsync(NewBrowser(), "/terms", "/feedback", new() { ["message"] = "   ", ["page"] = "//evil.example" });

        Assert.Equal("/feedback/thanks?result=empty&returnUrl=%2F", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SignedInFeedback_ShowsOnYourData_AndCanBeDeleted()
    {
        var browser = await SignInAsync(NewBrowser(), "data.owner@example.com");
        await PostFormAsync(browser, "/account", "/feedback", new() { ["message"] = "mine 1", ["page"] = "/account" });
        await PostFormAsync(browser, "/account", "/feedback", new() { ["message"] = "mine 2", ["page"] = "/account" });

        Assert.Matches("Feedback and contact messages you sent while signed in: <strong>2</strong>", await browser.GetStringAsync("/account/data"));

        var deleted = await PostFormAsync(browser, "/account/data", "/account/data/delete", new() { ["confirm"] = "true" });

        Assert.Equal("/account/data?deleted=2", deleted.Headers.Location!.OriginalString);
        Assert.DoesNotContain((await factory.Services.GetRequiredService<IFeedbackRepository>().ListAllAsync()), f => f.Message.StartsWith("mine ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FeedbackInbox_IsAdminOnly_AndResolvesItems()
    {
        var member = await SignInAsync(NewBrowser(), "inbox.member@example.com");
        Assert.Contains("/account/denied", (await member.GetAsync("/admin/feedback")).Headers.Location!.ToString());

        await PostFormAsync(NewBrowser(), "/terms", "/feedback", new() { ["message"] = "please resolve me", ["page"] = "/terms" });
        var id = (await factory.Services.GetRequiredService<IFeedbackRepository>().ListAllAsync()).Single(f => f.Message == "please resolve me").Id;

        var admin = await SignInAsync(NewBrowser(), AccountAppFactory.AdminEmail);
        Assert.Contains("please resolve me", await admin.GetStringAsync("/admin/feedback"));

        var resolved = await PostFormAsync(admin, "/admin/feedback", "/admin/feedback/resolve", new() { ["id"] = id });

        Assert.Equal("/admin/feedback?done=1", resolved.Headers.Location!.OriginalString);
        Assert.NotNull((await factory.Services.GetRequiredService<IFeedbackRepository>().ListAllAsync()).Single(f => f.Id == id).ResolvedAt);
    }

    [Fact]
    public async Task Feedback_WithoutAnAntiforgeryToken_IsRejected()
    {
        var response = await NewBrowser().PostAsync("/feedback", new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
