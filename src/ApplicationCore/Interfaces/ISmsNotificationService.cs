using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The operator- and reporting-facing operations over messages that have already been sent: bring
/// their delivery outcomes up to date from the provider, re-send one that did not reach the shopper,
/// dispose of a message's content, and reconcile the provider's records against this application's.
/// </summary>
public interface ISmsNotificationService
{
    /// <summary>
    /// Brings the stored delivery outcome of each non-terminal notification up to date by asking the
    /// provider for its current record. Best-effort: a provider hiccup leaves the stored value as-is.
    /// </summary>
    Task RefreshDeliveryOutcomesAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message identified by <paramref name="notificationId"/> to the same destination.
    /// Repeating the request under the same <paramref name="idempotencyKey"/> returns the message the
    /// first attempt produced without sending a second one; a fresh key is a genuine new attempt.
    /// </summary>
    Task<SmsNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of the content of the message identified by <paramref name="notificationId"/> at the
    /// provider and here, while keeping the fact it was sent and what became of it.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a reconciliation report over a date range: the provider's own record of messages sent
    /// from this application's configured number, lined up against what this application believes it
    /// sent, so a message one side knows about and the other does not is visible.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Raised when an operator acts on a notification id that does not exist.</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"No notification with id {notificationId} was found.") { }
}
