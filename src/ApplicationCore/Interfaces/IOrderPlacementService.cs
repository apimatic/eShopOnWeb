using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line in a placed order: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog items (no basket), reusing the Order aggregate.
/// The order starts <see cref="OrderStatus.AwaitingPayment"/>. Amounts come from catalog prices.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines);
}
