using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class NotificationReconciliationItem
{
    public string Match { get; init; } = string.Empty;
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }
    public int? NotificationId { get; init; }
    public string? Kind { get; init; }
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<NotificationReconciliationItem> Items { get; init; } = System.Array.Empty<NotificationReconciliationItem>();
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int LocalOnlyCount { get; init; }
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order);

    Task NotifyOrderDispatchedAsync(Order order);

    Task NotifyOrderCancelledAsync(Order order);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId);

    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey);

    Task<OrderNotification> RedactContentAsync(int notificationId);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
