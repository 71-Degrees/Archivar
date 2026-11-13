/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using System.Text.Json;
using Archivar.Core.Feedback;
using Archivar.Core.Storage;

namespace Archivar.Core.Tests.Storage;

public class FeedbackRepositoryTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly FeedbackRepository _repository;

    public FeedbackRepositoryTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "ArchivarFeedbackTests_" + Guid.NewGuid().ToString("N"));
        _repository = new FeedbackRepository(new FileDocumentStore(_tempFolder));
    }

    [Fact]
    public void Constructor_CreatesFolderIfMissing()
    {
        Assert.True(Directory.Exists(_tempFolder));
    }

    [Fact]
    public async Task SaveAsync_WritesOneJsonFilePerSubmission()
    {
        var submission = new FeedbackSubmission
        {
            Message = "The timer felt too fast on mobile",
            PageUrl = "https://trivianight.app/play/ABC123",
            SubmitterName = "Alice",
            IsGuest = false,
        };

        await _repository.SaveAsync(submission);

        var path = Path.Combine(_tempFolder, submission.Id + ".json");
        Assert.True(File.Exists(path));

        var json = await File.ReadAllTextAsync(path);
        var loaded = JsonSerializer.Deserialize<FeedbackSubmission>(json);

        Assert.NotNull(loaded);
        Assert.Equal("The timer felt too fast on mobile", loaded!.Message);
        Assert.Equal("https://trivianight.app/play/ABC123", loaded.PageUrl);
        Assert.Equal("Alice", loaded.SubmitterName);
        Assert.False(loaded.IsGuest);
    }

    [Fact]
    public async Task SaveAsync_TwoSubmissions_ProduceTwoSeparateFiles()
    {
        var first = new FeedbackSubmission { Message = "First" };
        var second = new FeedbackSubmission { Message = "Second" };

        await _repository.SaveAsync(first);
        await _repository.SaveAsync(second);

        Assert.Equal(2, Directory.GetFiles(_tempFolder, "*.json").Length);
    }

    [Fact]
    public async Task SaveAsync_InvalidId_Throws()
    {
        var submission = new FeedbackSubmission { Id = "../escape", Message = "x" };

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(submission));
    }

    [Fact]
    public async Task SaveAsync_SameIdTwice_OverwritesInPlace()
    {
        var submission = new FeedbackSubmission { Message = "v1" };
        await _repository.SaveAsync(submission);

        submission.Message = "v2";
        await _repository.SaveAsync(submission);

        var files = Directory.GetFiles(_tempFolder, "*.json");
        Assert.Single(files);

        var json = await File.ReadAllTextAsync(files[0]);
        var loaded = JsonSerializer.Deserialize<FeedbackSubmission>(json);
        Assert.Equal("v2", loaded!.Message);
    }

    [Fact]
    public async Task CountBySubmitterAsync_CountsOnlyThatSubmitter()
    {
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine 1", SubmitterId = "fb-1" });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine 2", SubmitterId = "fb-1" });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "someone else's", SubmitterId = "fb-2" });

        Assert.Equal(2, await _repository.CountBySubmitterAsync("fb-1"));
        Assert.Equal(1, await _repository.CountBySubmitterAsync("fb-2"));
        Assert.Equal(0, await _repository.CountBySubmitterAsync("fb-3"));
    }

    [Fact]
    public async Task DeleteForSubmitterAsync_RemovesOnlyThatSubmittersFiles()
    {
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine 1", SubmitterId = "fb-1" });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine 2", SubmitterId = "fb-1" });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "someone else's", SubmitterId = "fb-2" });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "guest, no submitter id" });

        var deleted = await _repository.DeleteForSubmitterAsync("fb-1");

        Assert.Equal(2, deleted);
        Assert.Equal(2, Directory.GetFiles(_tempFolder, "*.json").Length);
    }

    [Fact]
    public async Task DeleteForSubmitterAsync_UnknownSubmitter_DeletesNothing()
    {
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine", SubmitterId = "fb-1" });

        var deleted = await _repository.DeleteForSubmitterAsync("nobody");

        Assert.Equal(0, deleted);
        Assert.Single(Directory.GetFiles(_tempFolder, "*.json"));
    }

    [Fact]
    public async Task DeleteForSubmitterAsync_SkipsCorruptFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "corrupt.json"), "{ not valid json");
        await _repository.SaveAsync(new FeedbackSubmission { Message = "mine", SubmitterId = "fb-1" });

        var deleted = await _repository.DeleteForSubmitterAsync("fb-1");

        Assert.Equal(1, deleted);
        Assert.Single(Directory.GetFiles(_tempFolder, "*.json")); // the corrupt one is untouched, not deleted
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ListAll_IsNewestFirst_AndIncludesEveryKind()
    {
        await _repository.SaveAsync(new FeedbackSubmission { Message = "old", IsContentReport = true, SubmittedAt = DateTimeOffset.UtcNow.AddHours(-2) });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "new", SubmittedAt = DateTimeOffset.UtcNow });
        await _repository.SaveAsync(new FeedbackSubmission { Message = "middle", Source = "contact", SubmittedAt = DateTimeOffset.UtcNow.AddHours(-1) });

        var all = await _repository.ListAllAsync();

        Assert.Equal(["new", "middle", "old"], all.Select(r => r.Message));
    }

    [Fact]
    public async Task ResolveAsync_StampsIt_AndAnUnknownOrUnsafeIdIsFalse()
    {
        var report = new FeedbackSubmission { Message = "bad", IsContentReport = true };
        await _repository.SaveAsync(report);

        Assert.True(await _repository.ResolveAsync(report.Id));
        Assert.NotNull((await _repository.ListAllAsync()).Single().ResolvedAt);
        Assert.False(await _repository.ResolveAsync("nope"));
        Assert.False(await _repository.ResolveAsync("../evil"));
    }
}
