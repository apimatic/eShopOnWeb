using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record NotificationReconciliationItem(
    string? ProviderSid,
    int? NotificationId,
    string? LocalStatus,
    string? ProviderStatus,
    string Match);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<NotificationReconciliationItem> Matched,
    IReadOnlyList<NotificationReconciliationItem> ProviderOnly,
    IReadOnlyList<NotificationReconciliationItem> LocalOnly);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
