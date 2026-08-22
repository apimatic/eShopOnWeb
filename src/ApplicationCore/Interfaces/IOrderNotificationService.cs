using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<NotificationReconciliationItem> Matched { get; init; }
    public required IReadOnlyList<NotificationReconciliationItem> ProviderOnly { get; init; }
    public required IReadOnlyList<NotificationReconciliationItem> LocalOnly { get; init; }
}

public class NotificationReconciliationItem
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? LocalStatus { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
