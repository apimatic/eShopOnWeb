using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line on a new order: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog items (rather than from a basket), reusing the existing
/// Order/OrderItem model. The order starts awaiting payment.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default);
}
