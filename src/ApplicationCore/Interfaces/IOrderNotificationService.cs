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
    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledMessage> Matched { get; init; } = [];
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ReconciledMessage> EshopOnly { get; init; } = [];
}

public sealed class ReconciledMessage
{
    public int? NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EshopStatus { get; init; }
    public string? Kind { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
