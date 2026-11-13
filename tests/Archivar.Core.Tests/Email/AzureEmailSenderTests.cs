/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Azure.Communication.Email;
using Archivar.Core.Email;

namespace Archivar.Core.Tests.Email;

public class AzureEmailSenderTests
{
    private sealed class FakeTransport : IAzureEmailTransport
    {
        public EmailMessage? LastMessage { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Azure Communication Services returned HTTP 400: boom");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendAsync_BuildsMessageWithFromToSubjectAndBody()
    {
        var transport = new FakeTransport();
        var sender = new AzureEmailSender(transport, "alerts@bettertrivia.com");

        await sender.SendAsync("owner@example.com", "Something broke", "<p>Details</p>");

        Assert.NotNull(transport.LastMessage);
        Assert.Equal("alerts@bettertrivia.com", transport.LastMessage!.SenderAddress);
        Assert.Equal("owner@example.com", transport.LastMessage.Recipients.To[0].Address);
        Assert.Equal("Something broke", transport.LastMessage.Content.Subject);
        Assert.Equal("<p>Details</p>", transport.LastMessage.Content.Html);
    }

    [Fact]
    public async Task SendAsync_PropagatesTransportFailure()
    {
        var transport = new FakeTransport { ShouldThrow = true };
        var sender = new AzureEmailSender(transport, "alerts@bettertrivia.com");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync("owner@example.com", "Subject", "<p>Body</p>"));
    }
}
