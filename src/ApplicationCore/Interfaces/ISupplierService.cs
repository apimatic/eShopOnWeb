using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator-facing supplier operations: registering a supplier and starting a sync of its listing.
/// </summary>
public interface ISupplierService
{
    Task<Supplier> RegisterSupplierAsync(string name, string productListingUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a running sync for the supplier and queues it for background processing.
    /// Throws <see cref="Exceptions.SupplierNotFoundException"/> if the supplier does not exist.
    /// </summary>
    Task<CatalogSync> StartSyncAsync(int supplierId, CancellationToken cancellationToken = default);
}
