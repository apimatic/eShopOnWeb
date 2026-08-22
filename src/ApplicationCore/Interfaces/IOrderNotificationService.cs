using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(OrderNotificationDispatchContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);
    Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class OrderNotificationDispatchContext
{
    public required int OrderId { get; init; }
    public required string BuyerId { get; init; }
}

public sealed class NotificationReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<ReconciledNotification> Matched { get; init; }
    public required IReadOnlyList<ProviderOnlyNotification> ProviderOnly { get; init; }
    public required IReadOnlyList<ApplicationOnlyNotification> ApplicationOnly { get; init; }
}

public sealed class ReconciledNotification
{
    public required int NotificationId { get; init; }
    public required string ProviderMessageSid { get; init; }
    public required string ProviderStatus { get; init; }
    public required string ApplicationStatus { get; init; }
    public required string Kind { get; init; }
}

public sealed class ProviderOnlyNotification
{
    public required string ProviderMessageSid { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}

public sealed class ApplicationOnlyNotification
{
    public required int NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public required string Status { get; init; }
    public required string Kind { get; init; }
}
