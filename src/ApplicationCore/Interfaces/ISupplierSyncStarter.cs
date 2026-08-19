using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Creates and queues a supplier sync. Kept separate from the endpoint so the create-record +
/// enqueue steps are a single, request-scoped unit.
/// </summary>
public interface ISupplierSyncStarter
{
    /// <summary>
    /// Registers a new sync for the supplier and queues it for background execution.
    /// Returns null if the supplier does not exist.
    /// </summary>
    Task<CatalogSync?> StartAsync(Guid supplierId, CancellationToken cancellationToken = default);
}
