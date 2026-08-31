using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog items and quantities, reusing the app's existing
/// order/order-item model. Prices come from the catalog, not from the caller.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
