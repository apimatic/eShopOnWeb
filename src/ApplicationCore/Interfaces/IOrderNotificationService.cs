using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order);
    Task NotifyOrderDispatchedAsync(Order order);
    Task NotifyOrderCancelledAsync(Order order);
    Task CancelScheduledForDestinationAsync(string buyerId, string destinationE164);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId);
    Task<IReadOnlyList<OrderNotification>> ListForBuyerAsync(string buyerId);
    Task RefreshFromProviderAsync(IEnumerable<OrderNotification> notifications);
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey);
    Task<Result> RedactContentAsync(int notificationId);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledNotification> Matched { get; init; } = Array.Empty<ReconciledNotification>();
    public IReadOnlyList<ProviderOnlyMessage> ProviderOnly { get; init; } = Array.Empty<ProviderOnlyMessage>();
    public IReadOnlyList<ReconciledNotification> ApplicationOnly { get; init; } = Array.Empty<ReconciledNotification>();
}

public class ReconciledNotification
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public class ProviderOnlyMessage
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}
