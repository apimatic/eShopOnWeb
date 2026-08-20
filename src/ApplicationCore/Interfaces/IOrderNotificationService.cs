using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledMessage> Matched { get; init; } = [];
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ReconciledMessage> ApplicationOnly { get; init; } = [];
}

public sealed class ReconciledMessage
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? ApplicationStatus { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }
    public DateTimeOffset? ApplicationCreatedAt { get; init; }
}
