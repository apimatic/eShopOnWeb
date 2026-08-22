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
    Task CancelScheduledForContactAsync(int contactNumberId, CancellationToken cancellationToken = default);
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
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ProviderMessageSnapshot> ProviderOnly { get; init; } = Array.Empty<ProviderMessageSnapshot>();
    public IReadOnlyList<OrderNotification> EshopOnly { get; init; } = Array.Empty<OrderNotification>();
}

public class ReconciliationMatch
{
    public int NotificationId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? EshopStatus { get; init; }
    public string? ProviderStatus { get; init; }
}

public class ProviderMessageSnapshot
{
    public string Sid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public string? Body { get; init; }
}
