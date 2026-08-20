using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);
    Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool Truncated,
    IReadOnlyList<ReconciledNotification> Matched,
    IReadOnlyList<ReconciledNotification> ProviderOnly,
    IReadOnlyList<ReconciledNotification> ApplicationOnly);

public sealed record ReconciledNotification(
    int? NotificationId,
    string? ProviderSid,
    string? Status,
    string? Body,
    string? DateSent,
    int? ErrorCode,
    string? Source);
