using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator (administrator) actions on orders and their notifications. Each action is separately invocable.
/// </summary>
public interface IOperatorOrderService
{
    /// <summary>Mark an order dispatched, tell the shopper, and queue the delivery-feedback follow-up.</summary>
    Task<OperatorTransitionResult> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>Cancel an order, tell the shopper, and call off any pending follow-up.</summary>
    Task<OperatorTransitionResult> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on the key: the produced notification's
    /// id is returned; a repeat under the same key returns the same id without sending again.
    /// </summary>
    Task<ResendActionResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Dispose of a message's content at the provider and locally. Returns false if not found.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Reconcile the provider's record of this sender's messages against eShop's over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public enum OperatorTransitionResult
{
    /// <summary>The order does not exist.</summary>
    NotFound,
    /// <summary>The order was already in (or past) the target state — a no-op, nothing sent.</summary>
    NoOp,
    /// <summary>The transition happened and notifications were dispatched.</summary>
    Done
}

/// <summary>Result of a resend. <see cref="NotFound"/> true when the notification does not exist.</summary>
public sealed record ResendActionResult(bool NotFound, int NotificationId, bool WasDuplicate)
{
    public static ResendActionResult Missing() => new(true, 0, false);
}
