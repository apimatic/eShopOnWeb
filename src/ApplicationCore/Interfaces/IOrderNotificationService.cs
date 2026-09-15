using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record NotificationReconciliationItem(
    string ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? DateSent,
    int? EshopNotificationId,
    string Match);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<NotificationReconciliationItem> Items,
    int ProviderCount,
    int EshopCount,
    int MatchedCount,
    int MissingInEshopCount,
    int MissingInProviderCount);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task RefreshProviderStateAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
