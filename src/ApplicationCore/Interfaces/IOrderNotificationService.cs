using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record ReconciliationRow(
    string? EshopNotificationId,
    string? ProviderSid,
    string? Status,
    string Source);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> ProviderOnly,
    IReadOnlyList<ReconciliationRow> EshopOnly);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerOrdersAsync(string buyerId, IReadOnlyList<int> orderIds, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
    Task CancelPendingFollowUpsForDestinationAsync(string buyerId, string destinationCanonical, CancellationToken cancellationToken);
}
