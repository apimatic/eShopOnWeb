using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<IReadOnlyList<OrderNotification>> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciledMessage> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<LocalOnlyMessage> LocalOnly);

public sealed record ReconciledMessage(
    int NotificationId,
    string ProviderMessageSid,
    string LocalStatus,
    string ProviderStatus);

public sealed record ProviderOnlyMessage(
    string ProviderMessageSid,
    string ProviderStatus,
    string? DateSent);

public sealed record LocalOnlyMessage(
    int NotificationId,
    string? ProviderMessageSid,
    string LocalStatus);
