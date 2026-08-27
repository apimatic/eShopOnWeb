using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record NotificationResendResult(bool Success, OrderNotification? Notification, string? Error, bool WasIdempotentReplay = false);

public sealed record ReconciliationEntry(
    string? MessageSid,
    int? NotificationId,
    string? Status,
    DateTimeOffset? DateSent,
    string Match);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int LocalNotificationCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Orchestrates shopper SMS notifications as orders move. Provider failures
/// are recorded on the notification and never fail the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Refresh the stored delivery outcome of an order's messages from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    Task<NotificationResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's text both locally and at the provider.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
