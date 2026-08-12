using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and moves them through their lifecycle, keeping the shopper informed by SMS as it
/// goes. A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IShopperOrderService
{
    /// <summary>Places an order for the shopper from catalog items and tells them it was placed.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with
    /// the provider for a few days later. Operator action.
    /// </summary>
    Task<OrderOperationOutcome> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off the delivery follow-up before it goes
    /// out. Operator action.
    /// </summary>
    Task<OrderOperationOutcome> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one of the shopper's own orders, with each message's current outcome.
    /// Returns null when the order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(int orderId, string buyerId,
        CancellationToken cancellationToken = default);
}
