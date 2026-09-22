using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Atomically claims a caller-supplied resend idempotency key. The key is a primary key, so the
/// claim is established by an insert-or-fail against the store's unique-key constraint (caught
/// there), never a check-then-act read. How the persistence store enforces and recovers from the
/// duplicate is its concern; the orchestration only needs the claim result.
/// </summary>
public interface IResendIdempotencyStore
{
    /// <summary>
    /// Attempts to claim <paramref name="idempotencyKey"/> for <paramref name="notificationId"/>.
    /// Returns null when the key was newly claimed; returns the NotificationId recorded by the
    /// first request when the key was already used (so the caller can replay without sending again).
    /// </summary>
    Task<int?> TryClaimAsync(string idempotencyKey, int notificationId, CancellationToken ct);
}
