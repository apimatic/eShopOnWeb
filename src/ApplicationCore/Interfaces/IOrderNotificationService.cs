using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task SyncProviderStateAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<NotificationReconciliationEntry> Entries);

public record NotificationReconciliationEntry(
    string Match,
    int? NotificationId,
    string? ProviderMessageSid,
    string? EShopStatus,
    string? ProviderStatus);
