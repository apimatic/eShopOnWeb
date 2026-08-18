using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates order-progress SMS notifications: placing/dispatching/cancelling orders and the
/// messages that go out as they move, plus the operator actions over those messages. A message
/// that cannot be sent never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    // ---- Flow 1: the shopper's contact number -----------------------------------------------

    /// <summary>Register a mobile number for a shopper. The number is validated and normalised by
    /// the provider first; an unusable number is rejected. Returns the stored contact number.</summary>
    Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the caller's numbers. Returns false if it isn't theirs / doesn't exist.</summary>
    Task<bool> DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);

    // ---- Flow 2: messages as the order moves ------------------------------------------------

    /// <summary>Place an order for the shopper from catalog items, then message them that it was
    /// placed. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, ShippingAddressRequest? shippingAddress, CancellationToken cancellationToken = default);

    /// <summary>Mark an order dispatched: tell the shopper it's on its way and queue a delivery
    /// follow-up with the provider for a few days later. Returns false if the order doesn't exist.</summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel an order: tell the shopper, and call off any not-yet-sent follow-up so it
    /// never reaches them. Returns false if the order doesn't exist.</summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders. Ownership is enforced by <paramref name="ownerId"/>.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>The notifications for a set of orders, with delivery outcomes refreshed from the
    /// provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default);

    /// <summary>The notifications for one order (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Load one order (with items) by id, or null.</summary>
    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);

    // ---- Flow 3: what the operator can do about it ------------------------------------------

    /// <summary>Re-send a message that didn't reach the shopper. The idempotency key makes repeats
    /// safe: the same key never sends twice; a fresh key is a genuine new attempt. Returns null if
    /// the source notification doesn't exist.</summary>
    Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of the content of a message at the provider and locally, keeping the record
    /// that it was sent and what became of it. Returns false if the notification doesn't exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's record of this application's messages against eShop's own
    /// for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
