using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator actions on individual notifications: resend, dispose of content, and reconcile against
/// the provider. All are restricted to the administrator role at the API surface.
/// </summary>
public interface INotificationOperationsService
{
    /// <summary>
    /// Re-send a notification that did not reach the shopper. Idempotent on the caller-supplied key:
    /// repeating under the same key returns the earlier result without sending again; a fresh key is a
    /// genuine new attempt. Returns null if the notification does not exist.
    /// Throws <see cref="Exceptions.NotificationNotResendableException"/> if the message did reach the shopper.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the shopper's request. Afterwards the text is no longer
    /// retrievable from the provider either, while the fact that a message was sent — and what became
    /// of it — survives. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the provider's own record of messages from this application's sending number over a
    /// date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
