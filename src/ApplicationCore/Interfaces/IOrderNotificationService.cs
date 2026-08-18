using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the SMS notifications that accompany an order as it moves, and the operator actions on them.
///
/// The three order-event methods MUST NOT let a messaging failure fail the underlying operation — the
/// order is still placed, dispatched or cancelled, and a shopper with no number on file is simply not
/// messaged. The operator actions (re-send, redact, reconcile) DO surface provider failures, because
/// they are the caller's explicit request to make something happen at the provider.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed. Never throws for a messaging problem.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did delivery go?" follow-up with the
    /// provider for a few days later. Never throws for a messaging problem. Returns whether the order had
    /// already been dispatched or cancelled (in which case nothing new is sent).
    /// </summary>
    Task<OrderEventOutcome> NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any delivery follow-up still held by the
    /// provider so it never reaches them. Never throws for a messaging problem. Returns whether the order
    /// had already been cancelled (in which case nothing new is sent).
    /// </summary>
    Task<OrderEventOutcome> NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// repeating under the same key returns the first result without sending again; a fresh key sends anew.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally. Returns false if no such notification
    /// exists; throws <see cref="Exceptions.SmsGatewayException"/> if the provider redaction cannot be confirmed.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>The notifications for one order, with any still-changeable statuses refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default);

    /// <summary>A shopper's notifications, keyed by order id, with still-changeable statuses refreshed from the provider.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> GetOwnerNotificationsByOrderAsync(string ownerId, CancellationToken ct = default);

    /// <summary>
    /// Reconcile the provider's own record of messages for a range (restricted to the configured sending
    /// number) against what eShop believes it sent. Throws <see cref="Exceptions.SmsGatewayException"/> if the
    /// provider cannot be reached.
    /// </summary>
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
