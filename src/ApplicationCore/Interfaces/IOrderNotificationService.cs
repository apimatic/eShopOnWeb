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

    Task<IReadOnlyList<OrderNotification>> GetAndRefreshForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetAndRefreshForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<RedactNotificationResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ResendNotificationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool NotFound { get; init; }
    public bool DestinationNoLongerRegistered { get; init; }
    public int? NotificationId { get; init; }
}

public sealed class RedactNotificationResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }
}

public sealed class NotificationReconciliationReport
{
    public System.DateTimeOffset From { get; init; }
    public System.DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = [];
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = [];
}

public sealed class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public System.DateTimeOffset? DateSent { get; init; }
}
