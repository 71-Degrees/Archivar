/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using Archivar.Core.Feedback;

namespace Archivar.Core.Storage;

/// <summary>
/// Each feedback submission as its own document, named by its id: the durable record of a submission, kept whether or not the follow-up email notification succeeds. There is no general get - only
/// the content-report queue and the submitter-scoped count and delete the account pages need.
/// </summary>
public sealed class FeedbackRepository(IDocumentStore store, TimeProvider? timeProvider = null) : IFeedbackRepository
{
    public Task SaveAsync(FeedbackSubmission submission, CancellationToken cancellationToken = default) =>
        store.SetAsync(DocumentNames.For(submission.Id, "feedback"), submission, cancellationToken);

    public async Task<IReadOnlyList<FeedbackSubmission>> ListAllAsync(CancellationToken cancellationToken = default) =>
        (await ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => s.Submission)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

    public async Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!DocumentNames.IsValid(id))
        {
            return false;
        }

        var submission = await store.GetAsync<FeedbackSubmission>(DocumentNames.For(id, "feedback"), cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return false;
        }

        submission.ResolvedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
        await SaveAsync(submission, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> CountBySubmitterAsync(string submitterId, CancellationToken cancellationToken = default) =>
        (await FindBySubmitterAsync(submitterId, cancellationToken).ConfigureAwait(false)).Count;

    public async Task<int> DeleteForSubmitterAsync(string submitterId, CancellationToken cancellationToken = default)
    {
        var names = await FindBySubmitterAsync(submitterId, cancellationToken).ConfigureAwait(false);
        foreach (var name in names)
        {
            await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return names.Count;
    }

    private async Task<List<string>> FindBySubmitterAsync(string submitterId, CancellationToken cancellationToken) =>
        (await ReadAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(s => s.Submission.SubmitterId == submitterId)
            .Select(s => s.Name)
            .ToList();

    private async Task<List<(string Name, FeedbackSubmission Submission)>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var all = new List<(string, FeedbackSubmission)>();
        await foreach (var entry in store.ListAsync<FeedbackSubmission>(cancellationToken).ConfigureAwait(false))
        {
            all.Add(entry);
        }

        return all;
    }
}
