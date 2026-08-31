using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates shopper notifications as orders progress. Provider failures
/// must never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a failed notification under a caller-supplied
    /// idempotency key. Returns the notification produced by the resend, or the
    /// previously created one when the key was already used.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of the message text both locally and at the provider.</summary>
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes non-terminal provider statuses for the given notifications.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>Lines up the provider's record of messages against what eShop believes it sent.</summary>
    Task<NotificationReconciliation> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class NotificationReconciliation
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ProviderMessage> ProviderMessages { get; set; } = new();
    public List<string> MatchedMessageSids { get; set; } = new();
    public List<ProviderMessage> MissingLocally { get; set; } = new();
    public List<OrderNotification> MissingAtProvider { get; set; } = new();
}
