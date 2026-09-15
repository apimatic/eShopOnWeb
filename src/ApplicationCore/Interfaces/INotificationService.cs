using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator actions on individual notifications: re-sending one that did not reach the shopper,
/// disposing of a message's content, and reconciling eShop's record against the provider's.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Re-sends the message a notification represents. The caller-supplied idempotency key makes a
    /// repeat under the same key a no-op (returns the notification the first attempt produced), while a
    /// fresh key is a genuine new send. Returns null when the notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that it was sent, and what became of it, survives. Returns null when the
    /// notification does not exist.
    /// </summary>
    Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's record of messages sent from the configured number over a date range
    /// against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
