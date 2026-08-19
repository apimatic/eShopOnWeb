using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying operation — the order is still placed, dispatched or
/// cancelled and the caller's request still succeeds. A shopper with no number on file is simply
/// not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog items, reusing the existing order model,
    /// then tells the shopper it was placed.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken = default);

    /// <summary>Operator action: mark the order dispatched, tell the shopper, and queue a follow-up
    /// with the provider for a few days later. Returns false if the order doesn't exist.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel the order, tell the shopper, and call off any not-yet-sent
    /// follow-up so it never reaches them. Returns false if the order doesn't exist.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each showing where its notifications got to (statuses refreshed).</summary>
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order and what became of each. When <paramref name="ownerIdOrNullForOperator"/>
    /// is null the caller is an operator and any order is visible; otherwise the order must belong to
    /// the caller. Returns null when the order isn't visible / doesn't exist.
    /// </summary>
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string? ownerIdOrNullForOperator, CancellationToken cancellationToken = default);
}
