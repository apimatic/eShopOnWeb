using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for the given buyer directly from catalog item ids and quantities, reusing the
    /// existing order/order-item model. Item name, price and picture are snapshotted from the catalog
    /// at the time of ordering. Returns the persisted order (with its assigned id).
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemDetail> items, Address shippingAddress);
}

/// <summary>A requested line of an order: which catalog item, and how many.</summary>
public record OrderItemDetail(int CatalogItemId, int Quantity);
