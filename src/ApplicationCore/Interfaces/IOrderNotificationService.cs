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
    /// <summary>
    /// Validates the number with the provider and stores the provider's canonical form.
    /// Throws <see cref="Exceptions.PhoneNumberNotValidException"/> when the provider does
    /// not consider the number a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken ct);

    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a number owned by the buyer. Returns false when not found.</summary>
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct);

    /// <summary>Best-effort: a message that cannot be sent never fails the order operation.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct);

    /// <summary>Best-effort: tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct);

    /// <summary>Best-effort: tells the shopper and calls off any not-yet-sent follow-up at the provider.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Returns the order's notifications, optionally refreshing each one's delivery
    /// outcome from the provider first (there are no provider callbacks).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool syncWithProvider, CancellationToken ct);

    /// <summary>
    /// Re-sends the message of an existing notification. Repeating under an already-used
    /// idempotency key returns the notification that key produced without sending again.
    /// Throws <see cref="Exceptions.DomainRuleViolationException"/> when the original
    /// content has been disposed of.
    /// </summary>
    Task<ResendNotificationResult> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Disposes of the message text at the provider and locally. The record of the send
    /// and its outcome survive.
    /// </summary>
    Task RedactNotificationContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Lines the provider's own record of messages for the range up against what eShop
    /// believes it sent.
    /// </summary>
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
