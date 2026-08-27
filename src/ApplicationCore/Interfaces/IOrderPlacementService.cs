using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPlacementService
{
    /// <summary>
    /// Creates an order from catalog items. Returns null when any catalog item does not exist.
    /// </summary>
    Task<Order?> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address address, CancellationToken ct = default);
}
