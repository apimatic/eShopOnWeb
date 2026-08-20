using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's abstraction over PayPal. The Infrastructure implementation wraps the PayPal SDK and
/// translates every failure into <see cref="Exceptions.PayPalException"/>. Each write takes an idempotency
/// key so a retry/double-click replays the same PayPal-Request-Id instead of moving money twice.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorizes <paramref name="amount"/> (places a hold, does not capture). Creates a PayPal order with
    /// intent AUTHORIZE using the given card source, then authorizes it. The idempotency key is stable per
    /// order so a double-click does not authorize twice.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        decimal amount,
        CardPaymentSource source,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Captures an authorization (takes the money) and returns the amount/fee/net PayPal reported.</summary>
    Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Renews a stale authorization, returning the new authorization id to capture against.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Voids an authorization (releases the hold — no money moves).</summary>
    Task<VoidResult> VoidAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Refunds a capture in full (<paramref name="amount"/> null) or in part.</summary>
    Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Vaults a card and returns its token id plus a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string? existingCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(
        string paymentTokenId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every transaction PayPal's reporting holds for the date range, walking the whole range —
    /// chunked into PayPal's per-request window and paginated across every page, not just the first.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
