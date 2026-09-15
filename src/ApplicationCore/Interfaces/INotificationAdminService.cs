using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator-only actions over individual notifications.</summary>
public interface INotificationAdminService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. The caller-supplied idempotency key makes
    /// a repeat under the same key return the earlier result without sending again, while a fresh
    /// key is a genuine new attempt.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes a message's content at the provider (and locally), while the fact it was sent and
    /// what became of it survives.
    /// </summary>
    Task<DisposeResult> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Reconciles the provider's own record of messages against eShop's over a date range, counting
    /// only messages sent from the application's configured sending number.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
