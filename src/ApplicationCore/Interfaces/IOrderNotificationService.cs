using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

public sealed class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }
    public DateTimeOffset? EShopCreatedAt { get; init; }
}
