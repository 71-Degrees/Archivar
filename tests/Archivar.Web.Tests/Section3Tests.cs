/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Archivar.Web.Tests;

/// <summary>Release notes page, per-page SEO, and the build version.</summary>
public partial class Section3Tests(AccountAppFactory factory) : IClassFixture<AccountAppFactory>
{
    [GeneratedRegex("<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdBlock();

    private HttpClient NewBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/")]
    [InlineData("/terms")]
    [InlineData("/privacy")]
    [InlineData("/contact")]
    public async Task PublicPages_CarryCanonical_OpenGraph_AndValidJsonLd(string path)
    {
        var html = await NewBrowser().GetStringAsync(path);

        Assert.Contains($"<link rel=\"canonical\" href=\"http://localhost{(path == "/" ? "/" : path)}\"", html);
        Assert.Contains("<meta property=\"og:title\"", html);
        Assert.Contains("<meta name=\"description\"", html);
        using var schema = JsonDocument.Parse(JsonLdBlock().Match(html).Groups[1].Value);
        Assert.Equal(["WebPage", "Organization", "WebSite"], schema.RootElement.GetProperty("@graph").EnumerateArray().Select(n => n.GetProperty("@type").GetString()));
    }

    [Fact]
    public async Task Canonical_UsesThePublicAddress_WhenConfigured()
    {
        await using var site = factory.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://www.archivar.example"));
        // Requested on the public host itself, so the canonical-host redirect leaves it alone.
        var html = await site.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://www.archivar.example") }).GetStringAsync("/terms?utm=x");

        Assert.Contains("<link rel=\"canonical\" href=\"https://www.archivar.example/terms\"", html);
    }

    [Fact]
    public async Task ReleasesPage_IsAdminOnly_AndShowsThisServer()
    {
        var member = NewBrowser();
        await member.GetAsync("/dev/seed?as=member");
        Assert.Contains("/account/denied", (await member.GetAsync("/admin/releases")).Headers.Location!.ToString());

        var admin = NewBrowser();
        await admin.GetAsync("/dev/seed?as=admin");
        var html = await admin.GetStringAsync("/admin/releases");

        Assert.Contains("This server", html);
        Assert.Contains("profiles.byUser", html); // the cache table lists the account caches
    }

    [Fact]
    public async Task Version_ReportsTheBuildStampedFromGit()
    {
        using var doc = JsonDocument.Parse(await NewBrowser().GetStringAsync("/version"));

        Assert.Matches(@"^\d+\.\d+\.\d+(\+[0-9a-f]{8})?$", doc.RootElement.GetProperty("version").GetString());
    }
}
