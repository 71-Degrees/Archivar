/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Feedback;

namespace Archivar.Core.Storage;

public interface IFeedbackRepository
{
    Task SaveAsync(FeedbackSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>Every submission, newest first. Reads all feedback, so it is for the admin inbox only.</summary>
    Task<IReadOnlyList<FeedbackSubmission>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks a submission dealt with. False when there is no such submission.</summary>
    Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default);

    Task<int> CountBySubmitterAsync(string submitterId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every submission made by this submitter. Returns how many were deleted.</summary>
    Task<int> DeleteForSubmitterAsync(string submitterId, CancellationToken cancellationToken = default);
}
