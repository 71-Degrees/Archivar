/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Azure.Communication.Email;

namespace Archivar.Core.Email;

/// <summary>Sends through Azure Communication Services Email, as a verified address on the app's own domain (e.g. alerts@bettertrivia.com) - no
/// Gmail account, and no bytes leaving Azure. <paramref name="fromEmail"/> (constructor parameter) must exactly match the MailFrom address
/// configured on the linked Email Communication Service domain, or Azure rejects the send. Azure Communication Services has no display-name
/// concept for the sender - only the "from" address itself, set once on the domain - so <see cref="IEmailSender.SendAsync"/>'s fromName has
/// nowhere to go here and is intentionally unused.</summary>
public class AzureEmailSender : IEmailSender
{
    private readonly IAzureEmailTransport _transport;
    private readonly string _fromEmail;

    public AzureEmailSender(IAzureEmailTransport transport, string fromEmail)
    {
        _transport = transport;
        _fromEmail = fromEmail;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessage(_fromEmail, toEmail, new EmailContent(subject) { Html = htmlBody });
        return _transport.SendAsync(message, cancellationToken);
    }
}
