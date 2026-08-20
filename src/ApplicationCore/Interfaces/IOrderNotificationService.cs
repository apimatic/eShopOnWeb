using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task TryNotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task TryNotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task TryNotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IList<ReconciledMessage> Matched { get; init; } = new List<ReconciledMessage>();
    public IList<ProviderOnlyMessage> ProviderOnly { get; init; } = new List<ProviderOnlyMessage>();
    public IList<LocalOnlyNotification> LocalOnly { get; init; } = new List<LocalOnlyNotification>();
}

public class ReconciledMessage
{
    public int NotificationId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? LocalStatus { get; init; }
    public string? ProviderStatus { get; init; }
}

public class ProviderOnlyMessage
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public class LocalOnlyNotification
{
    public int NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
