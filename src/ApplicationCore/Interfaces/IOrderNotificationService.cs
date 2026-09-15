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

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciledNotification> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<ApplicationOnlyNotification> ApplicationOnly);

public record ReconciledNotification(
    int NotificationId,
    string ProviderMessageSid,
    string? ApplicationStatus,
    string? ProviderStatus);

public record ProviderOnlyMessage(
    string ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? DateSent);

public record ApplicationOnlyNotification(
    int NotificationId,
    string? ProviderMessageSid,
    string? ApplicationStatus);
