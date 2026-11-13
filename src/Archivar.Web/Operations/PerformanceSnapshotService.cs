/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Caching;

namespace Archivar.Web.Operations;

/// <summary>
/// Every five minutes, writes one line saying how much real storage was touched and how the caches did in that time. It lands in Application Insights (tagged
/// with the build version like every other request), so "did this release make the site read more?" is a query, not a guess:
/// <c>AppTraces | where Message startswith "Performance snapshot"</c>. See docs/performance.md.
/// </summary>
public class PerformanceSnapshotService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ILogger<PerformanceSnapshotService> _logger;
    private long _lastStorageCalls;
    private long _lastHits;
    private long _lastMisses;

    public PerformanceSnapshotService(ILogger<PerformanceSnapshotService> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Report();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("PerformanceSnapshotService.ExecuteAsync stopped: cancelled");
        }
    }

    private void Report()
    {
        try
        {
            var storage = StorageCalls.Total;
            var caches = CacheRegistry.Snapshot();
            var hits = caches.Sum(c => c.Hits);
            var misses = caches.Sum(c => c.Misses);

            var storageDelta = storage - _lastStorageCalls;
            var hitDelta = hits - _lastHits;
            var missDelta = misses - _lastMisses;
            (_lastStorageCalls, _lastHits, _lastMisses) = (storage, hits, misses);

            if (storageDelta == 0 && hitDelta == 0 && missDelta == 0)
            {
                return; // an idle five minutes is not worth a line
            }

            var ratio = hitDelta + missDelta == 0 ? 0 : 100.0 * hitDelta / (hitDelta + missDelta);
            _logger.LogInformation(
                "Performance snapshot: {StorageCalls} storage calls, {CacheHits} cache hits, {CacheMisses} cache misses ({HitRatioPercent:N0}% hit) in the last {Minutes} min",
                storageDelta, hitDelta, missDelta, ratio, (int)Interval.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Performance snapshot failed");
        }
    }
}
