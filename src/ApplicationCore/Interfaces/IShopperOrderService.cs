using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places and progresses orders through the existing order model, notifying the shopper by SMS as it
/// goes. A notification that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IShopperOrderService
{
    /// <summary>Places an order for the shopper from catalog item ids and quantities, then messages them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Operator action: marks an order dispatched, messages the shopper, and schedules the follow-up. Null if not found.</summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels an order, calls off any pending follow-up, and messages the shopper. Null if not found.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, each with the notifications sent for it and their latest outcome.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>An order by id, only if it belongs to the given shopper; otherwise null.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
