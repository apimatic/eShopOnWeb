using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never fails the caller's operation.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider. Never fails the caller's operation.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order was cancelled and calls off any follow-up that has not yet gone out. Never fails the caller's operation.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Calls off any provider-scheduled messages to a contact number that is being removed. Never fails the caller's operation.</summary>
    Task CancelScheduledForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default);

    /// <summary>The order's notifications with each message's delivery outcome refreshed from the provider (best effort per message).</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper. A repeated call
    /// under the same idempotency key returns the notification the first call produced without
    /// sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of the message's text at the provider and locally; the record and its outcome survive.</summary>
    Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Lines the provider's own record of messages for the range up against what eShop believes it sent.</summary>
    Task<NotificationReconciliation> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
