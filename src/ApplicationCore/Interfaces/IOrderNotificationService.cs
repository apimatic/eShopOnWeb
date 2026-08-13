using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and keeps shoppers informed by SMS as those orders move. Messaging is best-effort:
/// a message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog item ids/quantities and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress);

    /// <summary>Marks an order dispatched, tells the shopper it is on its way, and queues a delivery follow-up
    /// with the provider for a few days later.</summary>
    Task<Order> DispatchAsync(int orderId);

    /// <summary>Cancels an order, calls off any not-yet-sent follow-up, and tells the shopper it was cancelled.</summary>
    Task<Order> CancelAsync(int orderId);

    /// <summary>The caller's orders, each with its notifications and their current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId);

    /// <summary>The notifications for one of the caller's own orders, with current outcomes. Null if the order
    /// does not exist or is not the caller's.</summary>
    Task<OrderWithNotifications?> GetOrderNotificationsAsync(int orderId, string buyerId);

    /// <summary>Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// repeating a key returns the notification the first attempt produced without sending again.</summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey);

    /// <summary>Disposes of a message's content — redacting it at the provider and locally — while the record
    /// of the message and its outcome survives. Returns false if the notification does not exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId);

    /// <summary>Lines the provider's own record of messages from the configured sender up against what eShop
    /// believes it sent, over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications produced for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>The result of a reconciliation over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? Status,
    int? OrderId,
    string? Kind,
    DateTimeOffset? DateSent);
