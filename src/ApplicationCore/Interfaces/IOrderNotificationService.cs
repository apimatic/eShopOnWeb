using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<OrderNotification?> NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public IReadOnlyList<ReconciledMessage> Matched { get; init; } = Array.Empty<ReconciledMessage>();
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; init; } = Array.Empty<ReconciledMessage>();
    public IReadOnlyList<ReconciledMessage> ApplicationOnly { get; init; } = Array.Empty<ReconciledMessage>();
}

public sealed class ReconciledMessage
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? DeliveryStatus { get; init; }
    public string? ApplicationStatus { get; init; }
}
