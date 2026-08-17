using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders using the existing order model and messages the shopper as the order moves.
/// A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper and tells them it was placed.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched, tells the shopper it is on its way, and queues a "how did delivery go?"
    /// follow-up with the provider for a few days later. Returns null if the order does not exist.
    /// </summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order, tells the shopper, and calls off any delivery-feedback follow-up still queued
    /// with the provider so it can never reach them. Returns null if the order does not exist.
    /// </summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each decorated with its (freshly refreshed) notifications.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersWithNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one order, scoped to its owner and with delivery outcomes refreshed from the
    /// provider. Returns null when the order does not exist or does not belong to the caller (so one
    /// shopper can never see another's).
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetNotificationsForOwnerAsync(int orderId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key makes a repeated request
    /// a no-op (same result, no second message); a fresh key is a genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content on the provider side and locally, keeping the fact it was sent
    /// and what became of it. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's own record of sent messages against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many.</summary>
public sealed record OrderLine(int CatalogItemId, int Units);

/// <summary>An order paired with the notifications raised for it.</summary>
public sealed record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>Outcome of placing an order. On failure <see cref="Error"/> explains why (e.g. unknown catalog item).</summary>
public sealed record PlaceOrderResult(Order? Order, string? Error);

/// <summary>Outcome of a resend. <see cref="OriginalNotFound"/> distinguishes 404 from a validation error.</summary>
public sealed record ResendResult(Notification? Notification, bool AlreadyProcessed, bool OriginalNotFound, string? Error);
