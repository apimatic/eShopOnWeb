using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line of an API-placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Units);

/// <summary>
/// Coordinates the order lifecycle with the shopper-facing SMS notifications that accompany it.
/// A message that cannot be sent never fails the underlying operation — the order is still placed,
/// dispatched or cancelled, and every notification attempt is recorded so it can be reported on and
/// acted upon later.
/// </summary>
public interface IOrderNotificationService
{
    // ----- Contact numbers (shopper-scoped) -----

    /// <summary>
    /// Register a mobile number for a shopper. The number is validated with the provider and its
    /// canonical form stored. Throws when the provider does not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber);

    Task<IReadOnlyList<ContactNumber>> ListContactNumbersAsync(string buyerId);

    /// <summary>Remove one of the shopper's numbers. False if it does not exist or is not theirs.</summary>
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId);

    // ----- Orders -----

    /// <summary>Place an order for the shopper from catalog lines, then message them that it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines);

    /// <summary>
    /// Operator action: mark an order dispatched, message the shopper, and queue a delivery
    /// follow-up with the provider for a few days later. Null if the order does not exist.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId);

    /// <summary>
    /// Operator action: cancel an order, call off any not-yet-sent follow-up at the provider, and
    /// message the shopper. Null if the order does not exist.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId);

    /// <summary>The shopper's orders (with items). Refreshes non-terminal notification outcomes first.</summary>
    Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId);

    Task<Order?> GetOrderAsync(int orderId);

    /// <summary>The notifications for an order, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<Notification>> ListNotificationsForOrderAsync(int orderId);

    // ----- Operator notification actions -----

    Task<Notification?> GetNotificationAsync(int notificationId);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. The idempotency key makes
    /// a repeat under the same key return the existing resend rather than send a second message.
    /// </summary>
    Task<Notification> ResendNotificationAsync(int notificationId, string idempotencyKey);

    /// <summary>Operator action: dispose of a message's content at the provider.</summary>
    Task DisposeNotificationContentAsync(int notificationId);

    /// <summary>Operator action: reconcile eShop's records against the provider's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
