using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends SMS notifications as orders move and keeps the local notification records
/// in step with the provider. Messaging failures never fail the underlying order
/// operation: they are recorded on the notification instead.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Re-sends the message of a notification that did not reach the shopper.
    /// A repeat under an already-used idempotency key returns the original resend
    /// without sending again.</summary>
    Task<ResendResult> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message text at the provider and disposes of it locally.</summary>
    Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ResendResult
{
    public ResendResult(OrderNotification notification, bool alreadyExisted)
    {
        Notification = notification;
        AlreadyExisted = alreadyExisted;
    }

    public OrderNotification Notification { get; }
    public bool AlreadyExisted { get; }
}
