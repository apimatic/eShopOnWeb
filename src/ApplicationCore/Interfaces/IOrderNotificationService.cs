using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationItem
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>Matched | MissingLocally | MissingAtProvider | NotSent</summary>
    public string Disposition { get; set; } = string.Empty;
}

/// <summary>
/// Orchestrates order SMS notifications. Notification failures never fail the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under
    /// the same idempotency key returns the original resend without sending again.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text both locally and at the provider.</summary>
    Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels every not-yet-sent message addressed to a contact number, so a removed
    /// number is never messaged again — including provider-scheduled sends.
    /// </summary>
    Task CancelPendingForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default);

    /// <summary>Pulls the current delivery outcome from the provider for a non-terminal message.</summary>
    Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReconciliationItem>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
