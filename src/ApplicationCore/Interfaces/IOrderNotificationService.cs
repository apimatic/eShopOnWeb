using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and keeps shoppers informed by SMS as the order moves. A message that cannot be sent
/// never fails the underlying order operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order from catalog items for a shopper and tell them it was placed. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken);

    /// <summary>Operator dispatches the order: tell the shopper it is on its way and queue a delivery follow-up
    /// with the provider for a few days later. Returns false if the order does not exist.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator cancels the order: tell the shopper, and call off any follow-up that has not gone out.
    /// Returns false if the order does not exist.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The shopper's own orders, each with its notifications (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Load one of the shopper's own orders, or null if it does not exist / is not theirs.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken);

    /// <summary>Notifications for an order, with each message's current delivery outcome refreshed from the provider.</summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator re-sends a message that did not reach the shopper. Idempotent on the caller-supplied key:
    /// a repeat under the same key returns the notification already produced and sends nothing. Returns null if the
    /// source notification does not exist.</summary>
    Task<Notification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Operator disposes of a message's content at the provider (and locally). The record that the message
    /// was sent, and what became of it, survives. Returns false if the notification does not exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Operator reconciliation report: the provider's own record of messages from this app's sending number
    /// over a date range, lined up against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with its notifications.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);
