using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions over notifications: resend, content disposal, and reconciliation.</summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key does not send a second message; a fresh key sends a genuine second attempt.
    /// Returns the notification the resend produced.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the shopper's request so its text is no longer retrievable
    /// from the provider either, while the fact it was sent and what became of it survive. Returns
    /// false when the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's own record of messages sent from the application's configured
    /// sending number over a date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
