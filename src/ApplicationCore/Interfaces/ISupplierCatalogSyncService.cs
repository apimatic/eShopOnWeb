using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a supplier catalog sync: reads the supplier's listing, then creates or updates a
/// catalog item for every product found, matching by the supplier's own identifier so a
/// re-run never duplicates an already-imported product.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task ExecuteAsync(Guid syncId, CancellationToken cancellationToken = default);
}
