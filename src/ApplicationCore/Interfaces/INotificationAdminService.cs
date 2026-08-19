using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions over individual notifications and the reconciliation report.</summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. The caller-supplied idempotency key makes
    /// a repeat under the same key return the first result without sending again; a fresh key sends
    /// a new message. Returns the identifier of the message the resend produced.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the shopper's request: the text is redacted at the provider
    /// and cleared locally, while the fact a message was sent and what became of it survives. Returns
    /// false if the notification doesn't exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Builds the reconciliation report for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
