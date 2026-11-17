/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Archivar.Core.Accounts;
using WebCore.Accounts;
using WebCore.Caching;
using WebCore.Email;
using Archivar.Core.Storage;

namespace Archivar.Core.Operations;

/// <summary>Who a downtime announcement is emailed to. Only accounts that are enabled and have a confirmed email ever qualify.</summary>
public enum MaintenanceAudience
{
    Admins,
    Everyone,
}

/// <summary>The outcome of announcing downtime by email.</summary>
/// <param name="Recipients">The addresses that qualify.</param>
/// <param name="Sent">How many were actually sent (0 for a dry run, or when sending is switched off).</param>
public record MaintenanceEmailResult(IReadOnlyList<string> Recipients, int Sent, int Failed, bool DryRun);

/// <summary>
/// Scheduled-downtime notices (ported from TriviaNight): keeps the one current notice (shown as a site-wide banner, read
/// through a short cache so a page render never waits on storage) and emails it to admins or to every confirmed account.
/// </summary>
public class MaintenanceService : IDisposable
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    /// <summary>How many announcement emails go out at once.</summary>
    public const int MaxParallelSends = 8;

    private sealed record Held(MaintenanceNotice? Notice);

    private readonly IMaintenanceStore _store;
    private readonly IUserProfileRepository _profiles;
    private readonly IUserRepository _users;
    private readonly IEmailSender _email;
    private readonly bool _sendEmails;
    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _now;

    private readonly SnapshotCache<Held> _cache;
    private MaintenanceNotice? _lastKnown;

    public MaintenanceService(IMaintenanceStore store, IUserProfileRepository profiles, IUserRepository users, IEmailSender email, bool sendEmails, Func<DateTimeOffset>? now = null, ILogger<MaintenanceService>? logger = null)
    {
        _store = store;
        _profiles = profiles;
        _users = users;
        _email = email;
        _sendEmails = sendEmails;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _cache = new SnapshotCache<Held>("maintenanceNotice", CacheFor, _now);
    }

    /// <summary>The notice to show right now, or null. Cheap: served from memory for 30 seconds at a time.</summary>
    public async Task<MaintenanceNotice?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var held = await _cache.GetAsync(LoadAsync, cancellationToken).ConfigureAwait(false);
        return held.Notice is { } notice && notice.IsActiveAt(_now()) ? notice : null;
    }

    private async Task<Held> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _lastKnown = await _store.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A storage hiccup must never break page rendering: keep showing what we last knew.
            _logger.LogWarning(ex, "Could not read the maintenance notice; showing the last one known");
        }

        return new Held(_lastKnown);
    }

    public async Task SetAsync(MaintenanceNotice notice, CancellationToken cancellationToken = default)
    {
        await _store.SaveAsync(notice, cancellationToken).ConfigureAwait(false);
        _lastKnown = notice;
        _cache.Invalidate();
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        _lastKnown = null;
        _cache.Invalidate();
    }

    /// <summary>Everyone in <paramref name="audience"/> who should hear about downtime: enabled accounts with a confirmed email, once each.</summary>
    public async Task<IReadOnlyList<string>> ResolveRecipientsAsync(MaintenanceAudience audience, CancellationToken cancellationToken = default)
    {
        var accounts = (await _users.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(u => u.Id);
        var addresses = new List<string>();
        foreach (var profile in await _profiles.ListAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (profile.IsDisabled || (audience == MaintenanceAudience.Admins && profile.Role != Roles.Admin))
            {
                continue;
            }

            if (accounts.TryGetValue(profile.UserId, out var user) && user.EmailVerifiedAt is not null && !string.IsNullOrWhiteSpace(user.Email))
            {
                addresses.Add(user.Email.Trim());
            }
        }

        return addresses.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Emails the notice. With <paramref name="onlyTo"/> it goes to that one address instead (a deliberate test send);
    /// with <paramref name="dryRun"/> nothing is sent and the would-be recipients are just listed. Nothing is sent when email sending is
    /// switched off for this server (a dev machine), whatever else is asked.</summary>
    public async Task<MaintenanceEmailResult> NotifyAsync(MaintenanceNotice notice, MaintenanceAudience audience, bool dryRun, string? onlyTo = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> recipients = string.IsNullOrWhiteSpace(onlyTo)
            ? await ResolveRecipientsAsync(audience, cancellationToken).ConfigureAwait(false)
            : [onlyTo.Trim()];

        if (dryRun || !_sendEmails)
        {
            return new MaintenanceEmailResult(recipients, 0, 0, true);
        }

        // Bounded parallel sends: one at a time, a few thousand recipients would outrun the web request's timeout.
        var (subject, body) = Compose(notice);
        int sent = 0, failed = 0;
        await Parallel.ForEachAsync(recipients, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSends, CancellationToken = cancellationToken }, async (address, ct) =>
        {
            try
            {
                await _email.SendAsync(address, subject, body, ct).ConfigureAwait(false);
                Interlocked.Increment(ref sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref failed);
                _logger.LogWarning(ex, "A maintenance notice email could not be sent to one recipient; carrying on with the rest");
            }
        }).ConfigureAwait(false);

        return new MaintenanceEmailResult(recipients, sent, failed, false);
    }

    public static (string Subject, string Body) Compose(MaintenanceNotice notice)
    {
        var starts = notice.StartsAt.ToUniversalTime();
        var when = starts.ToString("dddd, MMMM d 'at' h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        var subject = "Archivar: scheduled downtime, " + starts.ToString("MMM d 'at' h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        var message = WebUtility.HtmlEncode(notice.Message).Replace("\n", "<br />");
        var body =
            "<p>Heads up from Archivar: the site will be briefly unavailable for an update.</p>" +
            $"<p><strong>{WebUtility.HtmlEncode(when)}</strong></p>" +
            (message.Length > 0 ? $"<p>{message}</p>" : string.Empty);
        return (subject, body);
    }

    /// <summary>Releases the disposable fields this type owns.</summary>
    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
