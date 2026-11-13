/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Archivar.Core.Email;
using Archivar.Web.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Archivar.Web.Tests;

public partial class OperationsTests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("(?:href|src)=\"([^\"]+\\.(?:css|js))\"")]
    private static partial Regex AssetReference();

    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ---- static assets ----------------------------------------------------------------------------------------------------------
    // MapStaticAssets does what TriviaNight needed StaticAssetCachePolicy + response compression for: a PUBLISHED build serves
    // fingerprinted files "max-age=31536000, immutable" with Brotli (checked live by `tools/Deploy perf`). The test host runs
    // unpublished, so here we check the part that makes that work: every reference is fingerprinted.

    [Fact]
    public async Task EveryStylesheetAndScript_IsReferencedByItsFingerprintedAddress()
    {
        var html = await NewBrowser().GetStringAsync("/");
        var assets = AssetReference().Matches(html).Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(assets);
        // A fingerprint is a content-hash segment ("app.d2hl9cvh80.css", "Archivar.Web.0jfhvr53dc.styles.css"); a plain "app.css"
        // would be cached with no way to bust it.
        Assert.All(assets, asset => Assert.Matches(@"\.[a-z0-9]{10}\.", asset));
    }

    [Fact]
    public async Task Pages_AreNeverCompressed()
    {
        // Pages carry antiforgery tokens; compressing secrets over HTTPS is what the BREACH attack feeds on.
        var request = new HttpRequestMessage(HttpMethod.Get, "/account/login");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
        using var response = await NewBrowser().SendAsync(request);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    // ---- canonical host ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithAPublicAddress_OtherHostNamesRedirectToIt_ButProbesDoNot()
    {
        await using var site = factory.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://www.archivar.example"));
        var browser = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://archivar-app.azurewebsites.net") });

        var page = await browser.GetAsync("/terms?x=1");
        Assert.Equal(HttpStatusCode.PermanentRedirect, page.StatusCode);
        Assert.Equal("https://www.archivar.example/terms?x=1", page.Headers.Location!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/healthz")).StatusCode);
    }

    // The refusal was once inert for weeks: its call had been joined onto the end of the comment line above it.
    [Theory]
    [InlineData("/wp-login.php")]
    [InlineData("/.env")]
    [InlineData("/.git/config")]
    public async Task ScannerRequests_GetABare404_NotTheNotFoundPage(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnOrdinaryMissingPage_StillGetsTheNotFoundPage()
    {
        var response = await factory.CreateClient().GetAsync("/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(string.Empty, await response.Content.ReadAsStringAsync());
    }

    // ---- dev switches -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DevSeed_SignsInAsTheSeedAdmin_AndLoggedOutSignsOut()
    {
        var browser = NewBrowser();

        var seeded = await browser.GetAsync("/dev/seed?as=admin");
        Assert.Equal("/admin/users", seeded.Headers.Location!.OriginalString);
        var usersPage = await browser.GetStringAsync("/admin/users");
        Assert.Contains("Seed Member", usersPage);
        Assert.Contains("Sample content report", await browser.GetStringAsync("/admin/feedback"));

        await browser.GetAsync("/dev/logged-out");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/account")).StatusCode);
    }

    [Fact]
    public async Task DevEndpoints_DoNotExistOutsideDevelopment()
    {
        await using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var response = await production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/dev/seed");

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- alert digest -----------------------------------------------------------------------------------------------------------

    private sealed class RecordingSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            lock (Sent)
            {
                Sent.Add((toEmail, subject, htmlBody));
            }

            return Task.CompletedTask;
        }
    }

    private static LogEvent Event(LogEventLevel level, string text) =>
        new(DateTimeOffset.UtcNow, level, null, new MessageTemplateParser().Parse(text), []);

    [Fact]
    public async Task AlertSink_IgnoresInformation_AndSendsAFatalAtOnce_HtmlEncoded()
    {
        var sender = new RecordingSender();
        using var sink = new EmailAlertSink(sender, "ops@example.com");

        sink.Emit(Event(LogEventLevel.Information, "just chatter"));
        sink.Emit(Event(LogEventLevel.Fatal, "<script>boom</script>"));
        for (var i = 0; i < 50 && sender.Sent.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        var (to, subject, body) = Assert.Single(sender.Sent);
        Assert.Equal("ops@example.com", to);
        Assert.StartsWith("Archivar: 1 alert(s) - Fatal", subject);
        Assert.DoesNotContain("just chatter", body);
        Assert.Contains("&lt;script&gt;boom&lt;/script&gt;", body);
    }
}
