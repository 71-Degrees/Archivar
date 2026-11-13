/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Email;

namespace Archivar.Web.Services;

/// <summary>
/// Stand-in for <see cref="IEmailSender"/> when no email provider (Gmail SMTP or the dormant
/// SendGrid fallback) has been configured yet, so nothing that depends on it (exception alerts,
/// feedback notifications) fails to resolve or throws - it just logs that the send was skipped.
/// See README's "Setting up email alerts" section.
/// </summary>
public class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Email not sent (no email provider configured): {Subject} -> {ToEmail}", subject, toEmail);
        return Task.CompletedTask;
    }
}
