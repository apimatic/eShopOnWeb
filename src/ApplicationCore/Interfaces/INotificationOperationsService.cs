using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator actions over notifications that have already been raised: re-sending a message that did
/// not reach the shopper, disposing of a message's content, and reconciling against the provider.
/// </summary>
public interface INotificationOperationsService
{
    /// <summary>
    /// Re-sends the message a notification carried. The idempotency key makes a repeat under the same
    /// key return the earlier result without sending again; a fresh key sends anew. Returns the id of
    /// the notification the resend produced.
    /// </summary>
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content so its text is no longer retrievable from the provider either,
    /// while the fact it was sent and what became of it survives.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Reconciles the provider's own record of messages sent from the configured sending number over
    /// a date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
