using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Executes a queued catalog sync: reads the supplier's listing and upserts every
/// product into the store catalog, updating the sync's status and counts as it goes.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task RunSyncAsync(Guid syncId, CancellationToken cancellationToken);
}
