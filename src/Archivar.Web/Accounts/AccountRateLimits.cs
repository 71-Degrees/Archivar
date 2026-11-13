/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Collections.Concurrent;
using WebCore.RateLimiting;

namespace Archivar.Web.Accounts;

/// <summary>
/// Limits on password sign-in and registration attempts: per caller (IP address) AND a global ceiling, so neither one
/// script nor a crowd of them can grind through passwords. Emailed codes have their own, stricter limits inside
/// <see cref="Core.Accounts.AccountEmailService"/>.
/// </summary>
public sealed class AccountRateLimits
{
    private const int MaxTrackedCallers = 5000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly SlidingWindowRateLimiter _global = new(maxInWindow: 60, Window);
    private readonly ConcurrentDictionary<string, SlidingWindowRateLimiter> _perCaller = new();

    /// <summary>True when this attempt may go ahead. The global counter is only spent once the caller's own limit allows it.</summary>
    public bool TryAcquire(HttpContext http)
    {
        if (_perCaller.Count > MaxTrackedCallers)
        {
            _perCaller.Clear();
        }

        var caller = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return _perCaller.GetOrAdd(caller, _ => new SlidingWindowRateLimiter(maxInWindow: 10, Window)).TryAcquire()
            && _global.TryAcquire();
    }
}
