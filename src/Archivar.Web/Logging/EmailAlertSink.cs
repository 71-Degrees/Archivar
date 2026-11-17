/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Net;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using WebCore.Email;

namespace Archivar.Web.Logging;

/// <summary>
/// Batches Warning/Error/Fatal log events and emails them as one digest, rather than one email
/// per event - flushes every <see cref="FlushInterval"/>, immediately once
/// <see cref="ImmediateFlushThreshold"/> events are buffered, or immediately on any Fatal event.
/// A logging sink must never throw, so the send itself is wrapped in a swallowing try/catch.
/// </summary>
public sealed class EmailAlertSink : ILogEventSink, IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);
    private const int ImmediateFlushThreshold = 20;

    private readonly IEmailSender _emailSender;
    private readonly string _alertEmail;
    private readonly object _lock = new();
    private readonly Timer _timer;
    private List<LogEvent> _buffer = new();

    public EmailAlertSink(IEmailSender emailSender, string alertEmail)
    {
        _emailSender = emailSender;
        _alertEmail = alertEmail;
        _timer = new Timer(_ => _ = FlushAsync(), null, FlushInterval, FlushInterval);
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning)
        {
            return;
        }

        bool flushNow;
        lock (_lock)
        {
            _buffer.Add(logEvent);
            flushNow = _buffer.Count >= ImmediateFlushThreshold || logEvent.Level == LogEventLevel.Fatal;
        }

        if (flushNow)
        {
            _ = FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        List<LogEvent> toSend;
        lock (_lock)
        {
            if (_buffer.Count == 0)
            {
                return;
            }

            toSend = _buffer;
            _buffer = new List<LogEvent>();
        }

        try
        {
            var subject = $"Archivar: {toSend.Count} alert(s) - {toSend.Max(e => e.Level)}";
            await _emailSender.SendAsync(_alertEmail, subject, BuildDigestHtml(toSend)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never let the sink throw - losing an alert email is better than crashing logging. A sink cannot log through itself, so the failure goes to Serilog's SelfLog (off unless enabled).
            SelfLog.WriteLine("EmailAlertSink could not send the alert digest: {0}", ex);
        }
    }

    private static string BuildDigestHtml(IReadOnlyList<LogEvent> events)
    {
        var sb = new StringBuilder();
        sb.Append("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;font-family:sans-serif;font-size:13px;'>");
        sb.Append("<tr><th>Time (UTC)</th><th>Level</th><th>Message</th><th>Exception</th></tr>");

        foreach (var e in events)
        {
            sb.Append("<tr>");
            sb.Append(CultureInfo.InvariantCulture, $"<td>{e.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}</td>");
            sb.Append(CultureInfo.InvariantCulture, $"<td>{e.Level}</td>");
            sb.Append(CultureInfo.InvariantCulture, $"<td>{WebUtility.HtmlEncode(e.RenderMessage(CultureInfo.InvariantCulture))}</td>");
            sb.Append(CultureInfo.InvariantCulture, $"<td><pre style='white-space:pre-wrap;margin:0;'>{WebUtility.HtmlEncode(e.Exception?.ToString() ?? string.Empty)}</pre></td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>Serilog disposes its sinks when the host shuts down (a restart, a deploy recycle): send whatever is still
    /// buffered - often the errors that led up to the restart - rather than drop it. Capped so a slow mail provider can't
    /// hold up shutdown.</summary>
    public void Dispose()
    {
        _timer.Dispose();
        FlushAsync().Wait(TimeSpan.FromSeconds(10));
    }
}
