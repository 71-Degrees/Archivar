/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Web;
using WebCore.Hosting.Security;
using WebCore.Hosting.Storage;
using Archivar.Core.Storage;
using Archivar.Web.Accounts;
using Archivar.Web.Components;
using WebCore.Caching;
using Archivar.Core.Operations;
using Archivar.Web.Feedback;
using Archivar.Web.Operations;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// Antiforgery adds its own X-Frame-Options: SAMEORIGIN by default, which would duplicate (and contradict) the DENY
// set below. The security-headers middleware owns that header.
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
// Where data lives: blob storage in production (Storage:ConnectionString + Storage:UseBlobStorage), folders under App_Data everywhere else.
var storage = new StorageBackend(builder.Configuration, builder.Environment.ContentRootPath);
builder.AddAccounts(storage);
// Feedback and contact messages: one JSON file each (Storage:FeedbackFolder overrides). Uncached - write-mostly; only the
// admin inbox and "Your data" read them (see docs/performance.md).
builder.Services.AddSingleton<IFeedbackRepository>(_ => StorageCalls.Wrap<IFeedbackRepository>(new FeedbackRepository(storage.Documents(AppStores.Feedback))));
builder.Services.AddSingleton<FeedbackService>();
builder.Services.AddHealthChecks();
var siteInfo = new SiteInfo(builder.Configuration["Site:BaseUrl"]);
builder.Services.AddSingleton(siteInfo);
builder.AddObservability(siteInfo);
builder.AddMaintenance(storage);
builder.Services.AddSingleton<ReleaseNotesService>();
builder.Services.AddSingleton<Archivar.Core.Archival.ILibrarySource, Archivar.Web.Archival.SampleLibrarySource>();
builder.Services.AddSingleton<Archivar.Core.Archival.IWatchHistorySource, Archivar.Web.Archival.SampleWatchHistorySource>();
builder.Services.AddSingleton<Archivar.Web.Archival.ArchivalReportService>();
builder.Services.AddHostedService<PerformanceSnapshotService>();

var app = builder.Build();
// Scanner requests (WordPress, .env, .git, PHP...) get a bare 404 before anything else runs, instead of rendering the whole not-found page.
app.UseProbeRequestRefusal();

var observability = app.Services.GetRequiredService<ObservabilityStatus>();
app.Logger.LogInformation("Application Insights: {AppInsights}; alert emails: {AlertEmails}", observability.ApplicationInsights ? "on" : "off", observability.AlertEmails ? "on" : "off");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Security headers on every response. The CSP allows only this site's own scripts, styles, fonts and connections: any
// third-party library gets vendored into wwwroot rather than loaded from a CDN. Loosen a directive only for a specific,
// understood need, and say why in this comment when you do. (`<ImportMap />` was removed from App.razor for the same
// reason - it renders an inline script that script-src 'self' would block.)
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    await next();
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// With a public address configured (Site:BaseUrl), a page request on any other host name - the *.azurewebsites.net name,
// the bare domain - is redirected (308) to the same page on the public address. /healthz, /version and /api/* are exempt
// so probes answer on any name; see CanonicalHostRedirect.
if (siteInfo.PublicUrl is not null)
{
    app.Use(async (context, next) =>
    {
        var target = CanonicalHostRedirect.TargetFor(
            siteInfo.PublicUrl,
            context.Request.Host.Value,
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Request.QueryString.Value ?? string.Empty,
            context.WebSockets.IsWebSocketRequest);
        if (target is null)
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = target;
    });
}

// One line per request (method, path, status, time) instead of the framework's several.
app.UseSerilogRequestLogging();

app.UseAuthentication();

// Development only, and only when Auth:DevAutoLoginEmail names an existing account: signs an unauthenticated request in
// as that account, to skip the login form while iterating locally. IsDevelopment() is checked here as well as by the
// config file only loading in Development, so it can never be reachable in production. Runs before UseAuthorization so
// [Authorize] pages see the signed-in user. /dev/logged-out switches it off for one browser (see DevEndpoints).
if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(app.Configuration["Auth:DevAutoLoginEmail"]))
{
    var devAutoLoginEmail = app.Configuration["Auth:DevAutoLoginEmail"]!;
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated != true && !context.Request.Cookies.ContainsKey(DevEndpoints.NoAutoLoginCookie))
        {
            var devUser = await context.RequestServices.GetRequiredService<IUserRepository>().GetByEmailAsync(devAutoLoginEmail);
            if (devUser is not null)
            {
                // SignInAsync only sets the cookie for the next request; replace this request's principal too.
                context.User = await AccountServices.SignInUserAsync(context, devUser);
            }
        }

        await next();
    });
}

app.UseAuthorization();
app.UseAntiforgery();

app.MapAccountEndpoints();
app.MapFeedbackEndpoints();
app.MapMaintenanceEndpoints();
app.MapDevEndpoints();
await app.SeedOwnerAsync();

app.MapHealthChecks("/healthz");

// Which build is running - the deploy tool polls this to confirm a deploy landed. Nothing sensitive.
app.MapGet("/version", (SiteInfo site) => Results.Json(new { version = site.Version, startedAt = site.StartedAt }));

// AI/search discoverability surface - see docs/ai-discoverability.md. Keep robots.txt, sitemap.xml and llms.txt in step
// with the list of public pages there. If a staging environment is added later, it must serve a blanket-disallow
// robots.txt and X-Robots-Tag: noindex instead (see TriviaNight's Program.cs for the pattern).
app.MapGet("/robots.txt", (SiteInfo site, HttpContext http) =>
{
    var baseUrl = site.BaseUrlFor(http);
    var robots =
        "User-agent: *\n" +
        "Allow: /\n" +
        "Disallow: /account\n" +
        "Disallow: /account/\n" +
        "Disallow: /admin/\n" +
        "Disallow: /healthz\n" +
        "Disallow: /version\n" +
        "Disallow: /not-found\n" +
        "Disallow: /Error\n" +
        "Disallow: /feedback\n" +
        "\n" +
        $"Sitemap: {baseUrl}/sitemap.xml\n";
    return Results.Text(robots, "text/plain");
});

app.MapGet("/sitemap.xml", (SiteInfo site, HttpContext http) =>
{
    var baseUrl = site.BaseUrlFor(http);
    var pages = new (string Path, string Priority)[]
    {
        ("/", "1.0"),
        ("/contact", "0.3"),
        ("/privacy", "0.3"),
        ("/terms", "0.3"),
    };

    var sb = new System.Text.StringBuilder();
    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
    sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
    foreach (var (path, priority) in pages)
    {
        sb.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(baseUrl + path))
          .Append("</loc><priority>").Append(priority).Append("</priority></url>\n");
    }

    sb.Append("</urlset>\n");
    return Results.Text(sb.ToString(), "application/xml");
});

// Hand-written, not generated. Says what is public and what is intentionally excluded.
app.MapGet("/llms.txt", (SiteInfo site, HttpContext http) =>
{
    var baseUrl = site.BaseUrlFor(http);
    var text =
        "# Archivar\n\n" +
        "> TODO: one-sentence description of Archivar.\n\n" +
        "## Pages\n" +
        $"- [Archivar]({baseUrl}/): Home page.\n" +
        $"- [Contact]({baseUrl}/contact): Send a message to the Archivar team.\n\n" +
        "## Policies\n" +
        $"- [Privacy Policy]({baseUrl}/privacy)\n" +
        $"- [Terms of Service]({baseUrl}/terms)\n\n" +
        "## Notes for AI agents\n" +
        "- Account and admin pages (/account, /account/*, /admin/*) require sign-in and are not public content.\n";
    return Results.Text(text, "text/plain");
});

app.MapStaticAssets();
// Blazor adds a second Content-Security-Policy header (frame-ancestors 'self') by default; the one set above already says
// frame-ancestors 'none', so switch Blazor's off rather than send two policies.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);

app.Run();

// Exposed so the integration tests can host the real app in memory (WebApplicationFactory<Program>).
public partial class Program;
