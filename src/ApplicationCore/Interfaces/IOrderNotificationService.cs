using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationItem(
    string? ProviderSid,
    int? NotificationId,
    string? Status,
    string? Body,
    string? DateSent,
    string Source);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationItem> Matched,
    IReadOnlyList<ReconciliationItem> OnlyInProvider,
    IReadOnlyList<ReconciliationItem> OnlyInApplication,
    bool Truncated);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
