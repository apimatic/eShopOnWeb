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
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<ReconciliationRow> Matched { get; init; }
    public required IReadOnlyList<ReconciliationRow> ProviderOnly { get; init; }
    public required IReadOnlyList<ReconciliationRow> LocalOnly { get; init; }
}

public sealed class ReconciliationRow
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? LocalStatus { get; init; }
    public string? ProviderStatus { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}
