using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from a set of catalog items and quantities for the given buyer,
    /// reusing the existing Order/OrderItem model. Unit prices are taken from the catalog
    /// (currency USD). The order starts <see cref="OrderPaymentStatus.AwaitingPayment"/>.
    /// Returns the persisted order (with its assigned id).
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IEnumerable<OrderItemRequest> items);
}

/// <summary>A requested line: a catalog item and how many of it.</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);
