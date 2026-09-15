using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog line on a new order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog items, reusing the existing order/order-item
    /// model, and tells the shopper their order was placed. A message that cannot be sent never
    /// fails the placement.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Marks the order dispatched: tells the shopper it is on its way and queues a "how did the
    /// delivery go?" follow-up with the provider for a few days later. Returns false if no such order.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Cancels the order: tells the shopper, and calls off any follow-up the provider has not yet
    /// sent so it can never reach them. Returns false if no such order.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's orders, each with the notifications sent for it and where they got to.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for one order and what became of each. Scoped: a shopper sees only their
    /// own order; an operator (admin) may see any.
    /// </summary>
    Task<(AccessOutcome Outcome, IReadOnlyList<Notification> Notifications)> GetOrderNotificationsAsync(
        int orderId, string requesterBuyerId, bool isAdmin, CancellationToken ct = default);
}
