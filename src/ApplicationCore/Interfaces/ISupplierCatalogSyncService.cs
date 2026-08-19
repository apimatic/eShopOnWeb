using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Runs a queued supplier sync: reads the supplier's listing, imports the products it finds
/// into the catalog (creating or updating, never duplicating), and records the outcome on the
/// sync record.
/// </summary>
public interface ISupplierCatalogSyncService
{
    Task ProcessSyncAsync(int syncId, CancellationToken cancellationToken = default);
}
