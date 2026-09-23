using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Flow 2 — placing an order and the messages that go out as it moves.</summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog items (reusing the existing Order/OrderItem model) and
    /// tells the shopper it was placed. A failure to message never fails order placement.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items,
        Address shipToAddress, CancellationToken ct);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with the
    /// provider for a few days later. No-op (and no messages) if the order is not currently placed.
    /// </summary>
    Task<OrderActionOutcome> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any not-yet-sent follow-up. No-op if already
    /// cancelled.
    /// </summary>
    Task<OrderActionOutcome> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// The notifications for one of the caller's orders, each refreshed from the provider so its current
    /// delivery outcome is reported. Returns null if the order is not the caller's or does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string ownerId, int orderId,
        bool refreshFromProvider, CancellationToken ct);

    /// <summary>The caller's orders, each with its notifications and where they got to.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string ownerId, CancellationToken ct);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public enum PlaceOrderOutcome
{
    Placed,
    InvalidItems,
    EmptyOrder
}

public record PlaceOrderResult(PlaceOrderOutcome Outcome, int? OrderId, string? Message);

public enum OrderActionOutcome
{
    NotFound,
    NoOp,
    Done
}

public record MyOrderView(Order Order, IReadOnlyList<OrderNotification> Notifications);
