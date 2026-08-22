using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, decimal total, CancellationToken ct);
    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken ct);
    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken ct);
    Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct);
    Task CancelPendingFollowUpsToNumberAsync(string canonicalNumber, CancellationToken ct);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId, CancellationToken ct);
    Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications, CancellationToken ct);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task RedactContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();
    public IReadOnlyList<ReconciliationEntry> EshopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

public sealed class ReconciliationEntry
{
    public int? NotificationId { get; init; }
    public string? ProviderSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EshopStatus { get; init; }
    public int? OrderId { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}
