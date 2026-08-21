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
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledNotification> Matched { get; init; } = Array.Empty<ReconciledNotification>();
    public IReadOnlyList<ReconciledNotification> ProviderOnly { get; init; } = Array.Empty<ReconciledNotification>();
    public IReadOnlyList<ReconciledNotification> ApplicationOnly { get; init; } = Array.Empty<ReconciledNotification>();
}

public class ReconciledNotification
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? Status { get; init; }
    public int? OrderId { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}
