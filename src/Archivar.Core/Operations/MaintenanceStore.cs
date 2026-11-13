/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Storage;
using Archivar.Core.Storage;

namespace Archivar.Core.Operations;

/// <summary>Where the current scheduled-downtime notice is kept, so every server instance (production and staging share it) shows the same one.</summary>
public interface IMaintenanceStore
{
    Task<MaintenanceNotice?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MaintenanceNotice notice, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>The notice is one document, <c>maintenance.json</c>, in the shared site-settings store.</summary>
public sealed class MaintenanceStore(IKeyedDocumentStore store) : IMaintenanceStore
{
    private const string DocumentName = "maintenance.json";

    public Task<MaintenanceNotice?> GetAsync(CancellationToken cancellationToken = default) => store.GetAsync<MaintenanceNotice>(DocumentName, cancellationToken);

    public Task SaveAsync(MaintenanceNotice notice, CancellationToken cancellationToken = default) => store.SetAsync(DocumentName, notice, cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken = default) => await store.DeleteAsync(DocumentName, cancellationToken).ConfigureAwait(false);
}
