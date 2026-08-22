using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task TryNotifyAsync(Order order, OrderNotificationKind kind, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelPendingForContactAsync(int contactNumberId, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciliationItem> Matched { get; init; } = Array.Empty<ReconciliationItem>();
    public IReadOnlyList<ReconciliationItem> ProviderOnly { get; init; } = Array.Empty<ReconciliationItem>();
    public IReadOnlyList<ReconciliationItem> EshopOnly { get; init; } = Array.Empty<ReconciliationItem>();
}

public class ReconciliationItem
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
    public string Match { get; init; } = string.Empty;
}
