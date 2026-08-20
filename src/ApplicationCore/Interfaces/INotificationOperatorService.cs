using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class OrderFulfillmentResult
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<NotificationView> Notifications { get; init; }
}

public sealed class ResendNotificationResult
{
    public required int NotificationId { get; init; }
    public required NotificationView Notification { get; init; }
    public required bool Replayed { get; init; }
}

public sealed class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<ReconciliationProviderOnly> ProviderOnly { get; init; }
    public required IReadOnlyList<ReconciliationApplicationOnly> ApplicationOnly { get; init; }
}

public sealed class ReconciliationMatch
{
    public required int NotificationId { get; init; }
    public required string ProviderMessageSid { get; init; }
    public required string ApplicationStatus { get; init; }
    public required string ProviderStatus { get; init; }
}

public sealed class ReconciliationProviderOnly
{
    public required string ProviderMessageSid { get; init; }
    public required string ProviderStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public sealed class ReconciliationApplicationOnly
{
    public required int NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public required string ApplicationStatus { get; init; }
}

public interface INotificationOperatorService
{
    Task<OrderFulfillmentResult> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderFulfillmentResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
