using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line: a catalog item and how many of it to order.</summary>
public record OrderItemQuantity(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Creates an order directly from catalog item ids and quantities (no basket), reusing the
    /// existing Order/OrderItem model. Unit prices are taken from the catalog. The returned order
    /// has been persisted and carries its generated id. It starts awaiting payment.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IEnumerable<OrderItemQuantity> items, Address shipToAddress);
}
