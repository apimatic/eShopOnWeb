using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Creates an order directly from catalog item ids and quantities (used by the
    /// PublicApi, which has no basket). Prices are read from the catalog. The order
    /// starts <see cref="OrderPaymentStatus.AwaitingPayment"/>. Returns the new order.
    /// </summary>
    Task<Order> CreateOrderAsync(
        string buyerId, IEnumerable<OrderItemRequest> items, Address shippingAddress);
}

/// <summary>A requested catalog item and quantity for a new order.</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);
