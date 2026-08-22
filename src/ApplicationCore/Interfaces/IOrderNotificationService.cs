using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string SendingNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledNotification> Matched { get; init; } = Array.Empty<ReconciledNotification>();
    public IReadOnlyList<ProviderOnlyMessage> ProviderOnly { get; init; } = Array.Empty<ProviderOnlyMessage>();
    public IReadOnlyList<ApplicationOnlyNotification> ApplicationOnly { get; init; } = Array.Empty<ApplicationOnlyNotification>();
}

public class ReconciledNotification
{
    public int NotificationId { get; init; }
    public string ProviderSid { get; init; } = string.Empty;
    public string? ApplicationStatus { get; init; }
    public string? ProviderStatus { get; init; }
}

public class ProviderOnlyMessage
{
    public string ProviderSid { get; init; } = string.Empty;
    public string? ProviderStatus { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public class ApplicationOnlyNotification
{
    public int NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ApplicationStatus { get; init; }
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
