/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Azure.Communication.Email;

namespace Archivar.Core.Email;

/// <summary>
/// The thin, mockable seam between <see cref="AzureEmailSender"/> and the real Azure
/// Communication Services Email API - lets tests exercise message construction without making a
/// real network call.
/// </summary>
public interface IAzureEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
