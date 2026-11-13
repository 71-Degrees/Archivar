/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Azure;
using Azure.Communication.Email;

namespace Archivar.Core.Email;

public sealed class AzureEmailTransport : IAzureEmailTransport
{
    private readonly EmailClient _client;

    public AzureEmailTransport(string connectionString)
    {
        _client = new EmailClient(connectionString);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // WaitUntil.Started returns as soon as Azure has accepted the send request, rather
            // than polling until final delivery - consistent with the other transports, which
            // treat "the provider accepted it" as success and don't block the caller on delivery.
            await _client.SendAsync(WaitUntil.Started, message, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Azure Communication Services returned HTTP {ex.Status}: {ex.Message}", ex);
        }
    }
}
