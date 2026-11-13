/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.AspNetCore.Mvc.Testing;

namespace Archivar.Web.Tests;

public class DiscoverabilityTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task RobotsTxt_AllowsTheSite_AndPointsAtTheSitemap()
    {
        var robots = await factory.CreateClient().GetStringAsync("/robots.txt");

        Assert.Contains("Allow: /\n", robots);
        Assert.DoesNotContain("Disallow: /\n", robots);
        Assert.Contains("/sitemap.xml", robots);
    }

    [Theory]
    [InlineData("/sitemap.xml", "application/xml")]
    [InlineData("/llms.txt", "text/plain")]
    public async Task DiscoveryFiles_AreServed(string path, string mediaType)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }
}
