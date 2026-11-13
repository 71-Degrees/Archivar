/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using Archivar.Core.Accounts;
using Archivar.Core.Storage;
using Archivar.Web.Operations;

namespace Archivar.Web.Tests;

public sealed class OwnerSeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "archivar-ownerseed-" + Guid.NewGuid().ToString("N"));
    private readonly UserRepository _users;
    private readonly UserProfileRepository _profiles;

    public OwnerSeedTests()
    {
        _users = new UserRepository(new FileDocumentStore(Path.Combine(_root, "users")));
        _profiles = new UserProfileRepository(new FileDocumentStore(Path.Combine(_root, "profiles")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Seed_CreatesPjj_AsAConfirmedAdmin_AndIsSafeToRunAgain()
    {
        var first = await OwnerSeed.EnsureAsync(_users, _profiles);
        var second = await OwnerSeed.EnsureAsync(_users, _profiles);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("PJJ", second.DisplayName);
        Assert.Equal(OwnerSeed.Email, second.Email);
        Assert.NotNull(second.EmailVerifiedAt);
        Assert.Equal(UserRole.Admin, (await _profiles.GetAsync(second.Id))!.Role);
        Assert.Single(await _users.ListAsync());
    }

    [Fact]
    public async Task Seed_KeepsAnExistingAccountsPassword_AndPromotesIt()
    {
        var existing = await _users.CreateAsync(new User { Email = OwnerSeed.Email, DisplayName = "PJJ", PasswordHash = PasswordHasher.Hash("already mine") });
        await _profiles.EnsureProfileAsync(existing.Id, "PJJ");
        await _profiles.SetDisabledAsync(existing.Id, true);

        await OwnerSeed.EnsureAsync(_users, _profiles);

        var after = await _users.GetByEmailAsync(OwnerSeed.Email);
        Assert.True(PasswordHasher.Verify("already mine", after!.PasswordHash));
        Assert.NotNull(after.EmailVerifiedAt);
        var profile = await _profiles.GetAsync(after.Id);
        Assert.Equal(UserRole.Admin, profile!.Role);
        Assert.False(profile.IsDisabled);
    }
}
