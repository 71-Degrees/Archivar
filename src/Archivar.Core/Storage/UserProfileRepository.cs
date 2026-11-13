/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using System.Collections.Concurrent;
using Archivar.Core.Accounts;

namespace Archivar.Core.Storage;

/// <summary>
/// One profile document per user, named by user id. The id is used directly (it is an account's GUID, or a numeric id from a since-removed login provider, both safe as a name) but is still validated,
/// because an id arriving in a claim is not fully within this app's control.
/// Every change is a read-modify-write, and two concurrent changes to the SAME user (several browser tabs signed into one account, all joining at once) would otherwise both read the old document and
/// then both write - a lost update at best and, on Windows, an exception out of the file move that once crashed a whole Blazor circuit. One lock per user id serializes that cycle without blocking
/// anyone else. In-process locking is enough because the app runs as a single instance whichever store backs it.
/// </summary>
public sealed class UserProfileRepository(IDocumentStore store, TimeProvider? timeProvider = null) : IUserProfileRepository
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public Task<UserProfile?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        store.GetAsync<UserProfile>(NameFor(userId), cancellationToken);

    public async Task<UserProfile> EnsureProfileAsync(string userId, string displayName, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                profile = new UserProfile { UserId = userId, DisplayName = displayName, Role = UserRole.Member };
            }
            else if (profile.DisplayName == displayName)
            {
                return profile;
            }
            else
            {
                profile.DisplayName = displayName;
            }

            await store.SetAsync(NameFor(userId), profile, cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task SetRoleAsync(string userId, UserRole role, CancellationToken cancellationToken = default) =>
        UpdateAsync(userId, profile => profile.Role = role, cancellationToken);


    public Task SetDisabledAsync(string userId, bool disabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            userId,
            profile =>
            {
                profile.IsDisabled = disabled;
                profile.DisabledAt = disabled ? (timeProvider ?? TimeProvider.System).GetUtcNow() : null;
            },
            cancellationToken);

    public async Task<IReadOnlyList<UserProfile>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<UserProfile>();
        await foreach (var (_, profile) in store.ListAsync<UserProfile>(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(profile);
        }

        return profiles.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Loads the user's profile, applies <paramref name="change"/>, and saves it, all under that user's lock. A user with no profile is left alone.</summary>
    private async Task UpdateAsync(string userId, Action<UserProfile> change, CancellationToken cancellationToken)
    {
        var gate = GateFor(userId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
            if (profile is null)
            {
                return;
            }

            change(profile);
            await store.SetAsync(NameFor(userId), profile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GateFor(string userId) => _userLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));

    private static string NameFor(string userId) => DocumentNames.For(userId, "user");
}
