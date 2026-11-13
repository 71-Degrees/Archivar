/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Archivar.Core.Storage;

namespace Archivar.Web.Tests;

/// <summary>The real app in memory (Development, so codes come back on screen instead of by email), with its own
/// throwaway storage folders.</summary>
public sealed class AccountAppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "boss@example.com";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "archivar-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:UsersFolder", Path.Combine(_root, "users"));
        builder.UseSetting("Storage:ProfilesFolder", Path.Combine(_root, "profiles"));
        builder.UseSetting("Storage:FeedbackFolder", Path.Combine(_root, "feedback"));
        builder.UseSetting("Storage:SiteSettingsFolder", Path.Combine(_root, "site-settings"));
        builder.UseSetting("Auth:AdminEmails", AdminEmail);
        builder.UseSetting("Auth:DevAutoLoginEmail", string.Empty);
        builder.UseSetting("DevSeed:Enabled", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

public partial class AccountFlowTests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("devCode=(\\d{6})")]
    internal static partial Regex DevCodePattern();

    internal HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> TokenFromAsync(HttpClient browser, string page)
    {
        var html = await browser.GetStringAsync(page);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {page}");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    internal static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string tokenPage, string action, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenFromAsync(browser, tokenPage);
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    /// <summary>Signs a fresh browser in (creating the account the first time) with an emailed code.</summary>
    internal static async Task<HttpClient> SignInWithCodeAsync(HttpClient browser, string email, string? displayName = null)
    {
        var sent = await PostFormAsync(browser, "/account/login", "/account/code-send", new() { ["email"] = email, ["returnUrl"] = "/" });
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        var code = DevCodePattern().Match(sent.Headers.Location!.OriginalString).Groups[1].Value;
        Assert.Matches("^\\d{6}$", code);

        var verified = await PostFormAsync(browser, "/account/login", "/account/code-verify",
            new() { ["email"] = email, ["code"] = code, ["returnUrl"] = "/account", ["displayName"] = displayName ?? string.Empty });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.Equal("/account", verified.Headers.Location!.OriginalString);
        return browser;
    }

    [Theory]
    [InlineData("/account")]
    [InlineData("/admin/users")]
    public async Task SignedOut_ProtectedPages_RedirectToSignIn(string path)
    {
        var response = await NewBrowser().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CodeSignIn_CreatesAConfirmedMemberAccount()
    {
        var browser = await SignInWithCodeAsync(NewBrowser(), "wave.rider@example.com", "Wave Rider");

        var account = await browser.GetStringAsync("/account");

        Assert.Contains("Wave Rider", account);
        Assert.Contains("Member", account);
        Assert.Contains("is confirmed", account);
    }

    [Fact]
    public async Task Member_IsDeniedTheAdminPage_AndTheAdminActions()
    {
        var browser = await SignInWithCodeAsync(NewBrowser(), "member@example.com");

        var page = await browser.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/account/denied", page.Headers.Location!.ToString());

        var action = await PostFormAsync(browser, "/account", "/admin/users/role", new() { ["userId"] = "anyone", ["role"] = "Admin" });
        Assert.Equal(HttpStatusCode.Redirect, action.StatusCode);
        Assert.Contains("/account/denied", action.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ConfiguredAdminEmail_BecomesAdmin_OnceConfirmed_AndCanPromoteAndDisable()
    {
        var member = await SignInWithCodeAsync(NewBrowser(), "promote.me@example.com", "Promote Me");
        var admin = await SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");

        // The role claim catches up on the request after sign-in (OnValidatePrincipal), no sign-out needed.
        var usersPage = await admin.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.OK, usersPage.StatusCode);
        var html = await usersPage.Content.ReadAsStringAsync();
        Assert.Contains("Promote Me", html);
        var memberId = (await factory.Services.GetRequiredService<IUserRepository>().GetByEmailAsync("promote.me@example.com"))!.Id;
        Assert.NotEmpty(memberId);

        // Promote: the member's own next request carries the Admin role claim.
        var promoted = await PostFormAsync(admin, "/admin/users", "/admin/users/role", new() { ["userId"] = memberId, ["role"] = "Admin" });
        Assert.Equal("/admin/users?done=role", promoted.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/admin/users")).StatusCode);

        // Disable: the member's session ends on their next request.
        var disabled = await PostFormAsync(admin, "/admin/users", "/admin/users/disabled", new() { ["userId"] = memberId, ["disabled"] = "true" });
        Assert.Equal("/admin/users?done=disabled", disabled.Headers.Location!.OriginalString);
        var afterDisable = await member.GetAsync("/account");
        Assert.Equal(HttpStatusCode.Redirect, afterDisable.StatusCode);
        Assert.Contains("/account/login", afterDisable.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_CannotChangeTheirOwnRole()
    {
        var admin = await SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");

        var html = await admin.GetStringAsync("/account");
        Assert.Contains("Admin", html);

        var selfId = (await factory.Services.GetRequiredService<IUserRepository>().GetByEmailAsync(AccountAppFactory.AdminEmail))!.Id;
        var response = await PostFormAsync(admin, "/admin/users", "/admin/users/role", new() { ["userId"] = selfId, ["role"] = "Member" });

        Assert.Equal("/admin/users?error=self", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Admin_CanMasqueradeAsAMember_SeeTheirAccount_AndResume()
    {
        var member = await SignInWithCodeAsync(NewBrowser(), "viewed@example.com", "Viewed Member");
        var admin = await SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");
        await admin.GetAsync("/admin/users"); // the role claim catches up
        var memberId = (await factory.Services.GetRequiredService<IUserRepository>().GetByEmailAsync("viewed@example.com"))!.Id;

        var start = await PostFormAsync(admin, "/admin/users", "/admin/users/masquerade", new() { ["userId"] = memberId });
        Assert.Equal("/wave", start.Headers.Location!.OriginalString);

        // Now the member to every check: their name and role, no admin page - and the way back in the header.
        var account = await admin.GetStringAsync("/account");
        Assert.Contains("Viewed Member", account);
        Assert.Contains("Resume as yourself", account);
        var adminPage = await admin.GetAsync("/admin/users");
        Assert.Contains("/account/denied", adminPage.Headers.Location!.ToString());

        // Resume works as the member (not an admin) and lands back on the admin page as the admin.
        var stop = await PostFormAsync(admin, "/account", "/account/masquerade-stop", []);
        Assert.Equal("/admin/users", stop.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/users")).StatusCode);
        Assert.DoesNotContain("Resume as yourself", await admin.GetStringAsync("/account"));
    }

    [Fact]
    public async Task Masquerade_RefusesYourself_ADisabledAccount_AndAnUnknownOne_AndAMemberCannotStartOne()
    {
        var member = await SignInWithCodeAsync(NewBrowser(), "off@example.com", "Switched Off");
        var other = await SignInWithCodeAsync(NewBrowser(), "other@example.com", "Other");
        var admin = await SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");
        await admin.GetAsync("/admin/users");
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var memberId = (await users.GetByEmailAsync("off@example.com"))!.Id;
        var adminId = (await users.GetByEmailAsync(AccountAppFactory.AdminEmail))!.Id;

        var self = await PostFormAsync(admin, "/admin/users", "/admin/users/masquerade", new() { ["userId"] = adminId });
        Assert.Equal("/admin/users?error=self", self.Headers.Location!.OriginalString);

        var unknown = await PostFormAsync(admin, "/admin/users", "/admin/users/masquerade", new() { ["userId"] = "nobody" });
        Assert.Equal("/admin/users?error=unknown", unknown.Headers.Location!.OriginalString);

        await PostFormAsync(admin, "/admin/users", "/admin/users/disabled", new() { ["userId"] = memberId, ["disabled"] = "true" });
        var disabled = await PostFormAsync(admin, "/admin/users", "/admin/users/masquerade", new() { ["userId"] = memberId });
        Assert.Equal("/admin/users?error=disabled", disabled.Headers.Location!.OriginalString);

        var byMember = await PostFormAsync(other, "/account", "/admin/users/masquerade", new() { ["userId"] = adminId });
        Assert.Contains("/account/denied", byMember.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Masquerade_CannotBeStackedOnAnotherMasquerade()
    {
        await SignInWithCodeAsync(NewBrowser(), "first@example.com", "First");
        var admin = await SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");
        await admin.GetAsync("/admin/users");
        var users = factory.Services.GetRequiredService<IUserRepository>();
        var firstId = (await users.GetByEmailAsync("first@example.com"))!.Id;

        // Make the viewed account an admin, so it could reach the start endpoint while masqueraded.
        await PostFormAsync(admin, "/admin/users", "/admin/users/role", new() { ["userId"] = firstId, ["role"] = "Admin" });
        await PostFormAsync(admin, "/admin/users", "/admin/users/masquerade", new() { ["userId"] = firstId });
        await admin.GetAsync("/account");

        var again = await PostFormAsync(admin, "/account", "/admin/users/masquerade", new() { ["userId"] = firstId });
        Assert.Equal("/admin/users?error=already", again.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Register_ThenPasswordSignIn_AndAWrongPasswordGetsTheGenericError()
    {
        var browser = NewBrowser();
        var registered = await PostFormAsync(browser, "/account/register", "/account/register-submit", new()
        {
            ["displayName"] = "Pat",
            ["email"] = "pat@example.com",
            ["password"] = "correct horse battery",
            ["confirmPassword"] = "correct horse battery",
            ["returnUrl"] = "/account",
        });
        Assert.Equal("/account", registered.Headers.Location!.OriginalString);
        Assert.Contains("isn't confirmed yet", await browser.GetStringAsync("/account"));

        var other = NewBrowser();
        var wrong = await PostFormAsync(other, "/account/login", "/account/login-submit", new() { ["email"] = "pat@example.com", ["password"] = "nope nope nope" });
        Assert.StartsWith("/account/login?error=1", wrong.Headers.Location!.OriginalString);

        var unknown = await PostFormAsync(other, "/account/login", "/account/login-submit", new() { ["email"] = "nobody@example.com", ["password"] = "nope nope nope" });
        Assert.StartsWith("/account/login?error=1", unknown.Headers.Location!.OriginalString);

        var right = await PostFormAsync(other, "/account/login", "/account/login-submit", new() { ["email"] = "pat@example.com", ["password"] = "correct horse battery", ["returnUrl"] = "/account" });
        Assert.Equal("/account", right.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ReturnUrl_CannotRedirectOffSite()
    {
        var browser = NewBrowser();
        var sent = await PostFormAsync(browser, "/account/login", "/account/code-send", new() { ["email"] = "safe@example.com", ["returnUrl"] = "//evil.example" });
        var code = DevCodePattern().Match(sent.Headers.Location!.OriginalString).Groups[1].Value;

        var verified = await PostFormAsync(browser, "/account/login", "/account/code-verify",
            new() { ["email"] = "safe@example.com", ["code"] = code, ["returnUrl"] = "//evil.example" });

        Assert.Equal("/", verified.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/account/login-submit")]
    [InlineData("/account/code-send")]
    [InlineData("/account/logout")]
    [InlineData("/account/reset-send")]
    [InlineData("/account/reset-submit")]
    public async Task FormPosts_WithoutAnAntiforgeryToken_AreRejected(string action)
    {
        var response = await NewBrowser().PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "a@example.com" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>Forgot / change password. Each test gets its own app (own storage and own sign-in rate limits - ten attempts per ten
/// minutes would run out), and some give the configured admin a password, which would make other tests' code sign-in refuse.</summary>
public sealed class PasswordFlowTests : IDisposable
{
    private readonly AccountAppFactory factory = new();

    public void Dispose() => factory.Dispose();

    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> SignInWithPasswordAsync(HttpClient browser, string email, string password) =>
        await AccountFlowTests.PostFormAsync(browser, "/account/login", "/account/login-submit", new() { ["email"] = email, ["password"] = password, ["returnUrl"] = "/account" });

    [Fact]
    public async Task ForgotPassword_ACodeResetsIt_TheOldOneStopsWorking_AndNobodyIsSignedIn()
    {
        var registered = await AccountFlowTests.PostFormAsync(NewBrowser(), "/account/register", "/account/register-submit", new()
        {
            ["displayName"] = "Forgetful", ["email"] = "forgetful@example.com", ["password"] = "old password 1", ["confirmPassword"] = "old password 1", ["returnUrl"] = "/account",
        });
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);

        var browser = NewBrowser();
        var sent = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-send", new() { ["email"] = "forgetful@example.com" });
        var code = AccountFlowTests.DevCodePattern().Match(sent.Headers.Location!.OriginalString).Groups[1].Value;
        Assert.Matches("^\\d{6}$", code);

        // A too-short password is refused without spending the code.
        var tooShort = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-submit", new() { ["email"] = "forgetful@example.com", ["code"] = code, ["password"] = "short", ["confirmPassword"] = "short" });
        Assert.Contains("passwordError=short", tooShort.Headers.Location!.OriginalString);

        var reset = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-submit", new() { ["email"] = "forgetful@example.com", ["code"] = code, ["password"] = "brand new pw 2", ["confirmPassword"] = "brand new pw 2" });
        Assert.StartsWith("/account/login?reset=1", reset.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/account")).StatusCode); // not signed in by the reset

        Assert.Contains("error=1", (await SignInWithPasswordAsync(NewBrowser(), "forgetful@example.com", "old password 1")).Headers.Location!.OriginalString);
        Assert.Equal("/account", (await SignInWithPasswordAsync(NewBrowser(), "forgetful@example.com", "brand new pw 2")).Headers.Location!.OriginalString);

        // The code is single-use.
        var again = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-submit", new() { ["email"] = "forgetful@example.com", ["code"] = code, ["password"] = "another pw 3", ["confirmPassword"] = "another pw 3" });
        Assert.Contains("codeError=expired", again.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ForgotPassword_AnUnknownAddressGetsTheSameAnswer_AndAWrongCodeChangesNothing()
    {
        var browser = NewBrowser();
        var unknown = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-send", new() { ["email"] = "nobody.here@example.com" });
        Assert.StartsWith("/account/forgot?step=code", unknown.Headers.Location!.OriginalString);

        await AccountFlowTests.SignInWithCodeAsync(NewBrowser(), "has.account@example.com");
        var other = NewBrowser();
        await AccountFlowTests.PostFormAsync(other, "/account/forgot", "/account/reset-send", new() { ["email"] = "has.account@example.com" });
        var wrong = await AccountFlowTests.PostFormAsync(other, "/account/forgot", "/account/reset-submit", new() { ["email"] = "has.account@example.com", ["code"] = "000000", ["password"] = "some password 9", ["confirmPassword"] = "some password 9" });
        Assert.Contains("codeError=wrongcode", wrong.Headers.Location!.OriginalString);
        Assert.Contains("error=1", (await SignInWithPasswordAsync(NewBrowser(), "has.account@example.com", "some password 9")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ForgotPassword_LetsAnAdminWhoLostTheirPasswordBackIn()
    {
        var admin = await AccountFlowTests.SignInWithCodeAsync(NewBrowser(), AccountAppFactory.AdminEmail, "Boss");
        await AccountFlowTests.PostFormAsync(admin, "/account", "/account/password-change", new() { ["password"] = "admin password 1", ["confirmPassword"] = "admin password 1" });
        Assert.Contains("error=usepassword", await UseCodeAsync(AccountAppFactory.AdminEmail));

        var browser = NewBrowser();
        var sent = await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-send", new() { ["email"] = AccountAppFactory.AdminEmail });
        var code = AccountFlowTests.DevCodePattern().Match(sent.Headers.Location!.OriginalString).Groups[1].Value;
        await AccountFlowTests.PostFormAsync(browser, "/account/forgot", "/account/reset-submit", new() { ["email"] = AccountAppFactory.AdminEmail, ["code"] = code, ["password"] = "admin password 2", ["confirmPassword"] = "admin password 2" });
        Assert.Equal("/account", (await SignInWithPasswordAsync(NewBrowser(), AccountAppFactory.AdminEmail, "admin password 2")).Headers.Location!.OriginalString);

        async Task<string> UseCodeAsync(string email)
        {
            var b = NewBrowser();
            var s = await AccountFlowTests.PostFormAsync(b, "/account/login", "/account/code-send", new() { ["email"] = email, ["returnUrl"] = "/" });
            var c = AccountFlowTests.DevCodePattern().Match(s.Headers.Location!.OriginalString).Groups[1].Value;
            return (await AccountFlowTests.PostFormAsync(b, "/account/login", "/account/code-verify", new() { ["email"] = email, ["code"] = c, ["returnUrl"] = "/account", ["displayName"] = string.Empty })).Headers.Location!.OriginalString;
        }
    }

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentOne_AndACodeOnlyAccountCanSetAFirstOne()
    {
        var browser = await AccountFlowTests.SignInWithCodeAsync(NewBrowser(), "changer@example.com", "Changer");
        Assert.DoesNotContain("Current password", await browser.GetStringAsync("/account"));

        var first = await AccountFlowTests.PostFormAsync(browser, "/account", "/account/password-change", new() { ["password"] = "first password 1", ["confirmPassword"] = "first password 1" });
        Assert.Equal("/account?passwordDone=1", first.Headers.Location!.OriginalString);
        Assert.Contains("Current password", await browser.GetStringAsync("/account"));

        var wrong = await AccountFlowTests.PostFormAsync(browser, "/account", "/account/password-change", new() { ["currentPassword"] = "not it", ["password"] = "second password 2", ["confirmPassword"] = "second password 2" });
        Assert.Equal("/account?passwordError=wrong", wrong.Headers.Location!.OriginalString);

        var mismatch = await AccountFlowTests.PostFormAsync(browser, "/account", "/account/password-change", new() { ["currentPassword"] = "first password 1", ["password"] = "second password 2", ["confirmPassword"] = "different" });
        Assert.Equal("/account?passwordError=mismatch", mismatch.Headers.Location!.OriginalString);

        var changed = await AccountFlowTests.PostFormAsync(browser, "/account", "/account/password-change", new() { ["currentPassword"] = "first password 1", ["password"] = "second password 2", ["confirmPassword"] = "second password 2" });
        Assert.Equal("/account?passwordDone=1", changed.Headers.Location!.OriginalString);
        Assert.Equal("/account", (await SignInWithPasswordAsync(NewBrowser(), "changer@example.com", "second password 2")).Headers.Location!.OriginalString);
    }
}
