/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace Archivar.Web.Feedback;

/// <summary>What to tell the sender for each <see cref="FeedbackSubmitStatus"/> the endpoint puts in the query string.</summary>
public static class FeedbackMessages
{
    public static string For(string? result) => result switch
    {
        "empty" => "Please enter a message first.",
        "toolong" => $"That's a bit long - please keep it under {FeedbackService.MaxMessageLength} characters.",
        "ratelimited" => "We're getting a lot of messages right now - please try again in a few minutes.",
        _ => "Something went wrong - please try again.",
    };
}
