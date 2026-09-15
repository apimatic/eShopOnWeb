using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciledMessage> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<ApplicationOnlyMessage> ApplicationOnly);

public record ReconciledMessage(
    int NotificationId,
    string ProviderMessageSid,
    string ApplicationStatus,
    string ProviderStatus);

public record ProviderOnlyMessage(
    string ProviderMessageSid,
    string Status,
    DateTimeOffset? DateCreated);

public record ApplicationOnlyMessage(
    int NotificationId,
    string? ProviderMessageSid,
    string Status);
