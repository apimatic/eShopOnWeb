using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<NotificationReconciliationItem> Items);

public record NotificationReconciliationItem(
    string Match,
    int? NotificationId,
    string? ProviderMessageSid,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? DateSent);
