/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Archivar.Web.Tests;

public class SecurityHeadersTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/robots.txt")]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp);
        // Same-origin websockets (Blazor) are already covered by 'self'; ws:/wss: would allow any host.
        Assert.Contains("connect-src 'self';", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }
}

public class FormEndpointGuardTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void EveryPostEndpoint_IsOnAnAntiforgeryFormGroup()
    {
        _ = factory.Server; // build the host so the endpoints exist
        var posts = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            // Blazor's own endpoints (circuit negotiation, its named-form handling on page routes) carry their own protection.
            .Where(e => !(e.RoutePattern.RawText ?? string.Empty).StartsWith("_blazor", StringComparison.Ordinal))
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Components.Endpoints.ComponentTypeMetadata>() is null)
            .ToList();

        Assert.NotEmpty(posts);
        var unguarded = posts.Where(e => e.Metadata.GetMetadata<FormAntiforgeryMetadata>() is null).Select(e => e.RoutePattern.RawText).ToList();
        Assert.True(unguarded.Count == 0, "POST endpoints not mapped on MapFormGroup (no antiforgery check): " + string.Join(", ", unguarded));
    }
}
