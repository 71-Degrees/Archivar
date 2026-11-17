/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Collections.Concurrent;
using System.Net;
using WebCore.Email;
using Archivar.Core.Feedback;
using WebCore.RateLimiting;
using Archivar.Core.Storage;

namespace Archivar.Web.Feedback;

public enum FeedbackSubmitStatus
{
    Accepted,
    Empty,
    TooLong,
    RateLimited,
}

/// <summary>
/// Ported from TriviaNight: saves feedback first (the durable record), then makes a best-effort attempt to email a
/// notification to <c>Email:AlertEmail</c> - an email outage never blocks the "thanks" the sender sees and never loses
/// the feedback. Spam guards: a length cap, a per-caller limit, and a global ceiling (TriviaNight had only the global
/// one); content reports get their own allowance so a burst of ordinary feedback can never block one.
/// </summary>
public sealed class FeedbackService(IFeedbackRepository repository, IEmailSender emailSender, IConfiguration configuration, ILogger<FeedbackService> logger)
{
    public const int MaxMessageLength = 4000;
    private const int MaxTrackedCallers = 5000;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly string _alertEmail = configuration["Email:AlertEmail"] ?? string.Empty;
    private readonly SlidingWindowRateLimiter _global = new(maxInWindow: 20, Window);
    private readonly SlidingWindowRateLimiter _reports = new(maxInWindow: 30, Window);
    private readonly ConcurrentDictionary<string, SlidingWindowRateLimiter> _perCaller = new();

    public async Task<FeedbackSubmitStatus> SubmitAsync(FeedbackSubmission submission, string callerKey, CancellationToken cancellationToken = default)
    {
        submission.Message = submission.Message.Trim();
        if (submission.Message.Length == 0)
        {
            return FeedbackSubmitStatus.Empty;
        }

        if (submission.Message.Length > MaxMessageLength)
        {
            return FeedbackSubmitStatus.TooLong;
        }

        if (_perCaller.Count > MaxTrackedCallers)
        {
            _perCaller.Clear();
        }

        var callerAllowed = _perCaller.GetOrAdd(callerKey, _ => new SlidingWindowRateLimiter(maxInWindow: 5, Window)).TryAcquire();
        if (!callerAllowed || !(submission.IsContentReport ? _reports : _global).TryAcquire())
        {
            logger.LogWarning("Feedback submission rate-limited");
            return FeedbackSubmitStatus.RateLimited;
        }

        await repository.SaveAsync(submission, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_alertEmail))
        {
            try
            {
                var subject = submission.IsContentReport ? "Archivar CONTENT REPORT" : submission.Source == "contact" ? "Archivar contact message" : "Archivar feedback";
                await emailSender.SendAsync(_alertEmail, subject, BuildHtml(submission), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to send feedback notification email for submission {SubmissionId}", submission.Id);
            }
        }

        return FeedbackSubmitStatus.Accepted;
    }

    // No inline style attributes, even in email: the message keeps its line breaks through <br />.
    private static string BuildHtml(FeedbackSubmission submission)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "-");
        var who = submission.IsGuest ? "a guest" : $"{E(submission.SubmitterName)} ({E(submission.SubmitterId)})";
        return $"""
            {(submission.IsContentReport ? "<p><strong>This is a report of inappropriate content.</strong> Review it on the admin Feedback page.</p>" : string.Empty)}
            <p><strong>From:</strong> {who}</p>
            <p><strong>Sent from:</strong> {E(submission.Source)}, page {E(submission.PageUrl)}</p>
            <p><strong>Submitted:</strong> {submission.SubmittedAt:yyyy-MM-dd HH:mm} UTC</p>
            <p><strong>Message:</strong><br />{E(submission.Message).ReplaceLineEndings("<br />")}</p>
            """;
    }
}
