using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Place an order directly from catalog items and quantities for a known buyer, reusing the existing
    /// order/order-item model. Returns the created order (with its assigned id).
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemInput> items, Address shippingAddress);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderItemInput(int CatalogItemId, int Quantity);
