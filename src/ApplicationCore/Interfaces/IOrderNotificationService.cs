using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ResendResult(OrderNotification Notification, bool WasDuplicate);

public record ReconciliationEntry(
    string ProviderMessageSid,
    string? ProviderStatus,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? LocalNotificationId,
    int? LocalOrderId,
    string? LocalStatus,
    string Disposition);

public interface IOrderNotificationService
{
    /// <summary>Notify the shopper their order was placed. Never throws for messaging failures.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Notify the shopper their order was dispatched and queue the delivery follow-up. Never throws for messaging failures.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Notify the shopper their order was cancelled and call off any unsent follow-up. Never throws for messaging failures.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>List the notifications for an order, refreshing non-terminal outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>List the notifications for a set of orders (last known outcomes, no provider refresh).</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send the message of a notification that did not reach the shopper.
    /// Repeating under the same idempotency key returns the original resend without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content both locally and at the provider.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Line up the provider's record of messages against what eShop believes it sent.</summary>
    Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
