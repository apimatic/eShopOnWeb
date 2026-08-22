using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken);

    Task CancelPendingFollowUpsForNumberAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListForOrderRefreshingAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    bool Truncated,
    IReadOnlyList<ReconciledMessage> Matched,
    IReadOnlyList<ReconciledMessage> ProviderOnly,
    IReadOnlyList<ReconciledMessage> EshopOnly);

public sealed record ReconciledMessage(
    int? NotificationId,
    string? ProviderSid,
    string? Status,
    string? DateSent);
