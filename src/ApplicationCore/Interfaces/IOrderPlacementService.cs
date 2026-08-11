using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line requested when placing an order directly from catalog items.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities (no basket), reusing the existing
/// Order/OrderItem model. Prices are taken from the catalog. The created order awaits payment.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);
}
