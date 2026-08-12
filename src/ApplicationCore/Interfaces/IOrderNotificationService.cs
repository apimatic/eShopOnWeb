using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications raised as an order moves through its lifecycle.
/// A messaging failure never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for a shopper from catalog items, reusing the app's existing Order/OrderItem
    /// model. Tells the shopper their order was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark the order dispatched. Tells the shopper it is on its way and queues a
    /// delivery-feedback follow-up with the provider for a few days later. Returns false if no such order.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel the order. Tells the shopper, and calls off any not-yet-sent
    /// follow-up so it never reaches them. Returns false if no such order.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications raised for one order, each with its own notification id and current delivery
    /// outcome. Visible to the order's owner; also visible to an operator (who acts on the ids).
    /// Returns null if the order does not exist or the caller may not see it.
    /// </summary>
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string callerBuyerId, bool callerIsOperator, CancellationToken cancellationToken = default);
}
