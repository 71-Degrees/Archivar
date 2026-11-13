/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Core.Feedback;

/// <summary>One message sent from the feedback button or the contact form. Ported from TriviaNight without its in-game context.</summary>
public class FeedbackSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Message { get; set; } = string.Empty;

    /// <summary>The page it was sent from (a site-relative path).</summary>
    public string? PageUrl { get; set; }

    /// <summary>"widget" or "contact".</summary>
    public string Source { get; set; } = "widget";

    public string? SubmitterName { get; set; }

    /// <summary>The submitter's account id, if they were signed in - lets "delete my data" find every submission that's
    /// actually theirs, since names aren't unique.</summary>
    public string? SubmitterId { get; set; }

    public bool IsGuest { get; set; } = true;

    /// <summary>True when the sender ticked "Report inappropriate content" rather than sending ordinary feedback. These
    /// are flagged for admins to act on.</summary>
    public bool IsContentReport { get; set; }

    /// <summary>When an admin marked it dealt with. Null while it is still open.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}
