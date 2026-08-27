using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPlacementService
{
    /// <summary>
    /// Places an order for the given buyer from catalog item ids and quantities,
    /// using the existing order/order-item model, and notifies the buyer that
    /// the order was placed. Notification failures never fail the order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress);
}
