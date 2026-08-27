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
    Task DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(int orderId, string? buyerId, bool isAdmin, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderEntry> ProviderOnly,
    IReadOnlyList<ReconciliationLocalEntry> LocalOnly,
    bool Incomplete);

public sealed record ReconciliationMatch(int NotificationId, string ProviderSid, string? Status);
public sealed record ReconciliationProviderEntry(string ProviderSid, string? Status, string? DateSent);
public sealed record ReconciliationLocalEntry(int NotificationId, string? ProviderSid, string? Status);
