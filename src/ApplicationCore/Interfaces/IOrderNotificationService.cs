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
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciledNotification> Matches,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<ReconciledNotification> EShopOnly);

public record ReconciledNotification(
    int NotificationId,
    string? ProviderMessageSid,
    string? Kind,
    string? Status,
    DateTimeOffset CreatedAt);

public record ProviderOnlyMessage(
    string? ProviderMessageSid,
    string? Status,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
