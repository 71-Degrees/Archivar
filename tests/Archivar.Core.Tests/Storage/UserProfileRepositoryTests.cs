/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using Archivar.Core.Accounts;
using Archivar.Core.Storage;

namespace Archivar.Core.Tests.Storage;

public class UserProfileRepositoryTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly UserProfileRepository _repository;

    public UserProfileRepositoryTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "ArchivarProfileTests_" + Guid.NewGuid().ToString("N"));
        _repository = new UserProfileRepository(new FileDocumentStore(_tempFolder));
    }

    [Fact]
    public void Constructor_CreatesFolderIfMissing()
    {
        Assert.True(Directory.Exists(_tempFolder));
    }

    [Fact]
    public async Task GetAsync_UnknownUser_ReturnsNull()
    {
        Assert.Null(await _repository.GetAsync("unknown"));
    }

    [Fact]
    public async Task EnsureProfileAsync_NewUser_CreatesMemberProfile()
    {
        var profile = await _repository.EnsureProfileAsync("user1", "Alice");

        Assert.Equal("user1", profile.UserId);
        Assert.Equal("Alice", profile.DisplayName);
        Assert.Equal(UserRole.Member, profile.Role);
        Assert.False(profile.IsAdmin);
    }

    [Fact]
    public async Task EnsureProfileAsync_ExistingUser_UpdatesDisplayNameButKeepsRole()
    {
        await _repository.EnsureProfileAsync("user1", "Alice");
        await _repository.SetRoleAsync("user1", UserRole.Admin);

        var profile = await _repository.EnsureProfileAsync("user1", "Alice Renamed");

        Assert.Equal("Alice Renamed", profile.DisplayName);
        Assert.Equal(UserRole.Admin, profile.Role);
    }

    [Fact]
    public async Task EnsureProfileAsync_SameDisplayName_DoesNotRewriteFile()
    {
        await _repository.EnsureProfileAsync("user1", "Alice");
        var path = Path.Combine(_tempFolder, "user1.json");
        var writeTimeBefore = File.GetLastWriteTimeUtc(path);

        await Task.Delay(10);
        await _repository.EnsureProfileAsync("user1", "Alice");

        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task SetRoleAsync_UnknownUser_NoOps()
    {
        await _repository.SetRoleAsync("unknown", UserRole.Admin);

        Assert.Null(await _repository.GetAsync("unknown"));
    }

    [Fact]
    public async Task SetRoleAsync_KnownUser_UpdatesRole()
    {
        await _repository.EnsureProfileAsync("user1", "Alice");
        await _repository.SetRoleAsync("user1", UserRole.Admin);

        var profile = await _repository.GetAsync("user1");

        Assert.Equal(UserRole.Admin, profile!.Role);
        Assert.True(profile.IsAdmin);
    }

    [Fact]
    public async Task SetDisabledAsync_UnknownUser_NoOps()
    {
        await _repository.SetDisabledAsync("unknown", true);

        Assert.Null(await _repository.GetAsync("unknown"));
    }

    [Fact]
    public async Task SetDisabledAsync_True_SetsFlagAndStampsDisabledAt()
    {
        await _repository.EnsureProfileAsync("user1", "Alice");
        await _repository.SetDisabledAsync("user1", true);

        var profile = await _repository.GetAsync("user1");

        Assert.True(profile!.IsDisabled);
        Assert.NotNull(profile.DisabledAt);
    }

    [Fact]
    public async Task SetDisabledAsync_False_ClearsFlagAndDisabledAt()
    {
        await _repository.EnsureProfileAsync("user1", "Alice");
        await _repository.SetDisabledAsync("user1", true);
        await _repository.SetDisabledAsync("user1", false);

        var profile = await _repository.GetAsync("user1");

        Assert.False(profile!.IsDisabled);
        Assert.Null(profile.DisabledAt);
    }

    [Fact]
    public async Task ListAllAsync_ReturnsAllProfilesOrderedByName()
    {
        await _repository.EnsureProfileAsync("user1", "Zoe");
        await _repository.EnsureProfileAsync("user2", "Alice");

        var all = await _repository.ListAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Alice", all[0].DisplayName);
        Assert.Equal("Zoe", all[1].DisplayName);
    }

    [Fact]
    public async Task GetAsync_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "user1.json"), "{ not valid json");

        Assert.Null(await _repository.GetAsync("user1"));
    }

    [Fact]
    public async Task GetPathFor_InvalidUserId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.GetAsync("../escape"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
        GC.SuppressFinalize(this);
    }
}
