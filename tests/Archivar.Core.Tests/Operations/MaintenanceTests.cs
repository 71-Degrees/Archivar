/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Accounts;
using WebCore.Email;
using WebCore.Storage;
using Archivar.Core.Accounts;
using Archivar.Core.Operations;
using Archivar.Core.Storage;

namespace Archivar.Core.Tests.Operations;

public class MaintenanceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "archivar-maint-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingSender _sender = new();
    private readonly UserRepository _users;
    private readonly UserProfileRepository _profiles;
    private DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public MaintenanceTests()
    {
        _users = new UserRepository(new FileDocumentStore(Path.Combine(_folder, "users")));
        _profiles = new UserProfileRepository(new FileDocumentStore(Path.Combine(_folder, "profiles")), Roles.All);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = new();

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            lock (Sent)
            {
                Sent.Add((toEmail, subject, htmlBody));
            }

            return Task.CompletedTask;
        }
    }

    private MaintenanceService Service(bool sendEmails = true) =>
        new(new MaintenanceStore(new FileDocumentStore(Path.Combine(_folder, "site"))), _profiles, _users, _sender, sendEmails, () => _now);

    private async Task AddAsync(string email, UserRole role, bool verified = true, bool disabled = false)
    {
        var user = await _users.CreateAsync(new User { Email = email, DisplayName = email, EmailVerifiedAt = verified ? _now : null });
        await _profiles.EnsureProfileAsync(user.Id, email);
        await _profiles.SetRoleAsync(user.Id, role.ToString());
        if (disabled)
        {
            await _profiles.SetDisabledAsync(user.Id, true);
        }
    }

    [Fact]
    public async Task Recipients_AreConfirmedEnabledAdmins_OrEveryConfirmedAccount()
    {
        await AddAsync("admin@example.com", UserRole.Admin);
        await AddAsync("member@example.com", UserRole.Member);
        await AddAsync("unconfirmed@example.com", UserRole.Admin, verified: false);
        await AddAsync("disabled@example.com", UserRole.Member, disabled: true);

        Assert.Equal(["admin@example.com"], await Service().ResolveRecipientsAsync(MaintenanceAudience.Admins));
        Assert.Equal(["admin@example.com", "member@example.com"], await Service().ResolveRecipientsAsync(MaintenanceAudience.Everyone));
    }

    [Fact]
    public async Task Notify_DryRunSendsNothing_ButListsRecipients()
    {
        await AddAsync("admin@example.com", UserRole.Admin);

        var result = await Service().NotifyAsync(new MaintenanceNotice { StartsAt = _now.AddHours(2), Message = "Update" }, MaintenanceAudience.Admins, dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(new[] { "admin@example.com" }, result.Recipients);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Notify_SendsToEveryRecipient_AndEscapesTheMessage()
    {
        await AddAsync("admin@example.com", UserRole.Admin);
        await AddAsync("member@example.com", UserRole.Member);

        var result = await Service().NotifyAsync(new MaintenanceNotice { StartsAt = _now.AddHours(2), Message = "Back <soon> & better" }, MaintenanceAudience.Everyone, dryRun: false);

        Assert.Equal(2, result.Sent);
        Assert.Equal(2, _sender.Sent.Count);
        Assert.Contains("Back &lt;soon&gt; &amp; better", _sender.Sent[0].Body);
        Assert.Contains("scheduled downtime", _sender.Sent[0].Subject);
    }

    [Fact]
    public async Task Notify_OnlyTo_SendsJustThatAddress()
    {
        await AddAsync("admin@example.com", UserRole.Admin);

        var result = await Service().NotifyAsync(new MaintenanceNotice { StartsAt = _now.AddHours(2) }, MaintenanceAudience.Admins, dryRun: false, onlyTo: "me@example.com");

        Assert.Equal(1, result.Sent);
        Assert.Equal("me@example.com", _sender.Sent.Single().To);
    }

    [Fact]
    public async Task Notify_WhenSendingIsOff_NeverSends()
    {
        await AddAsync("admin@example.com", UserRole.Admin);

        var result = await Service(sendEmails: false).NotifyAsync(new MaintenanceNotice { StartsAt = _now.AddHours(2) }, MaintenanceAudience.Admins, dryRun: false);

        Assert.True(result.DryRun);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Notice_ShowsUntilItExpires_ThenClears()
    {
        var service = Service();
        await service.SetAsync(new MaintenanceNotice { StartsAt = _now.AddHours(1), EndsAt = _now.AddHours(2), Message = "x" });

        Assert.NotNull(await service.GetActiveAsync());
        _now = _now.AddHours(3);
        Assert.Null(await service.GetActiveAsync());

        await service.SetAsync(new MaintenanceNotice { StartsAt = _now.AddMinutes(10), Message = "y" });
        Assert.NotNull(await service.GetActiveAsync());
        await service.ClearAsync();
        Assert.Null(await service.GetActiveAsync());
    }

    [Fact]
    public async Task Notice_IsSharedThroughTheStore()
    {
        await Service().SetAsync(new MaintenanceNotice { StartsAt = _now.AddHours(1), Message = "shared" });

        // A second server instance (a fresh service over the same storage) sees it after its own cache lapses.
        Assert.Equal("shared", (await Service().GetActiveAsync())?.Message);
    }
}
