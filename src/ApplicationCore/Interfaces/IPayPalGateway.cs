using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port to PayPal. Every method translates PayPal SDK success into a domain DTO and every PayPal
/// failure (provider error, transport failure, unreadable body) into a
/// <see cref="Exceptions.PaymentGatewayException"/>, so the application layer has a single failure type
/// to handle and never sees an SDK type. Implemented in Infrastructure over the PayPal Server SDK.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>The configured settlement currency (from PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>
    /// Places a hold equal to the order total (does not take the money). Creates a PayPal order with
    /// intent AUTHORIZE funded by the request's card or saved-card vault id, then authorizes it.
    /// Idempotent via a deterministic PayPal-Request-Id derived from the request seed.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct);

    /// <summary>
    /// Captures (takes) the held funds at fulfilment. Throws with
    /// <see cref="Exceptions.PaymentGatewayException.AuthorizationExpired"/> set when the hold has gone
    /// stale and must be renewed first.
    /// </summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Renews a stale hold so fulfilment can proceed. Throws with
    /// <see cref="Exceptions.PaymentGatewayException.AuthorizationUnrenewable"/> set when it can no longer
    /// be renewed (past the reauthorization window).
    /// </summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Releases a hold before fulfilment (cancel). No money moves.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Refunds a capture, fully (<paramref name="amount"/> null) or partially. The idempotency key is
    /// used verbatim as PayPal-Request-Id so a repeat under the same key never refunds twice.
    /// </summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card and returns its token id plus a safe descriptor for display.</summary>
    Task<VaultedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct);

    /// <summary>Removes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// Lists PayPal's own transaction records for a date range, across every page, for reconciliation.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
