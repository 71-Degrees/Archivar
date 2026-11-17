/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Archivar.Web.Tests;

/// <summary>
/// Behind a reverse proxy every request arrives from the proxy's address. With ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
/// (a go-live App Setting - see docs/dev-and-ops.md) the real client IP comes from X-Forwarded-For, so the per-IP limits
/// (sign-in, registration, feedback) keep visitors apart instead of putting everyone in one bucket.
/// </summary>
public partial class ForwardedHeadersTests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static async Task<string> TryPasswordAsync(HttpClient browser, string clientIp, string email)
    {
        var match = TokenPattern().Match(await browser.GetStringAsync("/account/login"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login-submit")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value,
                ["email"] = email,
                ["password"] = "wrong password",
            }),
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        var response = await browser.SendAsync(request);
        return response.Headers.Location!.OriginalString;
    }

    [Fact]
    public async Task WithForwardedHeadersOn_OneClientHittingTheLimit_DoesNotLockOutAnother()
    {
        await using var site = factory.WithWebHostBuilder(b => b.UseSetting("FORWARDEDHEADERS_ENABLED", "true"));
        var browser = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // A different account each time, so it is the address's allowance that runs out (one account has its own allowance from any address).
        for (var i = 0; i < 10; i++)
        {
            Assert.StartsWith("/account/login?error=1", await TryPasswordAsync(browser, "203.0.113.10", $"nobody{i}@example.com"));
        }

        Assert.StartsWith("/account/login?error=ratelimited", await TryPasswordAsync(browser, "203.0.113.10", "nobody10@example.com"));
        Assert.StartsWith("/account/login?error=1", await TryPasswordAsync(browser, "198.51.100.20", "somebody@example.com"));
    }
}
