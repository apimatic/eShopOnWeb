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
    Task CancelOutstandingMessagesForNumberAsync(string buyerId, string destinationNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledNotification> Matched { get; init; } = Array.Empty<ReconciledNotification>();
    public IReadOnlyList<ProviderMessageRecord> ProviderOnly { get; init; } = Array.Empty<ProviderMessageRecord>();
    public IReadOnlyList<ApplicationNotificationRecord> ApplicationOnly { get; init; } = Array.Empty<ApplicationNotificationRecord>();
}

public sealed class ReconciledNotification
{
    public int NotificationId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string ApplicationStatus { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
}

public sealed class ProviderMessageRecord
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? DateSent { get; init; }
}

public sealed class ApplicationNotificationRecord
{
    public int NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
