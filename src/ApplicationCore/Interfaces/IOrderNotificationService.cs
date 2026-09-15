using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyList<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ProviderOnlyMessage> ProviderOnly { get; init; } = Array.Empty<ProviderOnlyMessage>();
    public IReadOnlyList<ApplicationOnlyNotification> ApplicationOnly { get; init; } = Array.Empty<ApplicationOnlyNotification>();
}

public sealed class ReconciliationMatch
{
    public int NotificationId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
    public string ApplicationStatus { get; init; } = string.Empty;
    public NotificationKind Kind { get; init; }
}

public sealed class ProviderOnlyMessage
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Direction { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public sealed class ApplicationOnlyNotification
{
    public int NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string Status { get; init; } = string.Empty;
    public NotificationKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
