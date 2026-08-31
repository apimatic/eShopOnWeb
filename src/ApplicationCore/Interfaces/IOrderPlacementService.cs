using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog items (item id + quantity), reusing the app's existing order and
/// order-item model. Unlike the storefront checkout, this does not go through a basket — it is the entry
/// point the API needs so the invoicing flow is drivable end to end without the Web host.
/// </summary>
public interface IOrderPlacementService
{
    /// <summary>
    /// Snapshots the given catalog items into a new order owned by <paramref name="buyerId"/> and returns
    /// its id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: which catalog item, and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
