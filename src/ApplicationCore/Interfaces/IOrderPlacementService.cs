using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from a set of catalog items and quantities, reusing the app's existing
/// order/order-item model. This complements <see cref="IOrderService"/> (which places an order from a
/// basket) so an order can be placed through the API without a basket round-trip.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);
}

/// <summary>A requested catalog item and quantity for an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
