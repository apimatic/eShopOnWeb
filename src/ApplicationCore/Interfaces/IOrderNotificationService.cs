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

    Task<IReadOnlyList<OrderNotification>?> ListForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> ListForOrdersAsync(
        IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool Truncated,
    IReadOnlyList<ReconciledMessage> Matched,
    IReadOnlyList<ReconciledMessage> ProviderOnly,
    IReadOnlyList<ReconciledMessage> ApplicationOnly);

public sealed record ReconciledMessage(
    string? ProviderSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationStatus,
    string? DateSent);
