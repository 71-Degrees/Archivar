/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Email;
using Archivar.Web.Accounts;
using Archivar.Web.Logging;
using Serilog;
using Serilog.Events;

namespace Archivar.Web.Operations;

/// <summary>
/// Logging and telemetry, ported from TriviaNight. Serilog replaces the default logging provider so every ILogger call
/// (and framework entries such as Blazor circuit exceptions) flows through one pipeline:
/// <list type="bullet">
/// <item>Console, always.</item>
/// <item>An emailed digest of Warning/Error/Fatal events (<see cref="EmailAlertSink"/>) - outside Development, and only when
/// real email and <c>Email:AlertEmail</c> are configured.</item>
/// <item>Application Insights, when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set: requests, dependencies, exceptions and
/// every log line, tagged with the build version. Unset = off, never a startup failure.</item>
/// </list>
/// </summary>
public static class Observability
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, SiteInfo site)
    {
        var config = builder.Configuration;

        // Serilog's own console sink replaces the framework's console/debug/event-log providers. Cleared BEFORE App Insights
        // registers its provider, so writeToProviders below forwards to App Insights only - otherwise every line prints twice.
        builder.Logging.ClearProviders();

        // Read explicitly rather than trusting the SDK's own auto-detection, so "is it on?" is one visible decision.
        var appInsightsConnectionString = config["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var appInsightsConfigured = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        if (appInsightsConfigured)
        {
            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
                options.ApplicationVersion = site.Version;
            });
        }

        var alertEmail = config["Email:AlertEmail"];
        var sendAlerts = AccountServices.IsEmailConfigured(config) && !string.IsNullOrWhiteSpace(alertEmail) && !builder.Environment.IsDevelopment();

        // writeToProviders: true also forwards every event to the Microsoft.Extensions.Logging providers - which is how the
        // Application Insights logger provider receives Serilog's events alongside its own request telemetry.
        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);

            if (sendAlerts)
            {
                logger.WriteTo.Sink(new EmailAlertSink(services.GetRequiredService<IEmailSender>(), alertEmail!), restrictedToMinimumLevel: LogEventLevel.Warning);
            }
        }, writeToProviders: true);

        builder.Services.AddSingleton(new ObservabilityStatus(appInsightsConfigured, sendAlerts));
        return builder;
    }
}

/// <summary>What was switched on at startup - logged once, so a missing connection string is visible rather than silent.</summary>
public sealed record ObservabilityStatus(bool ApplicationInsights, bool AlertEmails);
