using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator-facing operations for registering suppliers and running/reading catalog syncs.
/// </summary>
public interface ISupplierSyncService
{
    /// <summary>Registers a supplier and returns it (with its assigned id).</summary>
    Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a sync for the supplier and queues it for background processing. Returns the created
    /// sync (with its assigned id) without waiting for the work to finish. Null if the supplier is unknown.
    /// </summary>
    Task<SupplierSync?> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);

    /// <summary>Returns the current state of a sync, or null if no such sync exists.</summary>
    Task<SupplierSync?> GetSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
