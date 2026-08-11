using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The single seam between this application and PayPal. Every PayPal interaction goes through the
/// one implementation of this interface (which owns the paypal-sdk); the rest of the app speaks only
/// in the neutral contracts above. The <paramref name="idempotencyKey"/> parameters are sent to PayPal
/// as its request-id so a repeated call never moves money twice.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>Creates an order with intent=AUTHORIZE and authorizes it with a raw card (one-off payment).</summary>
    Task<AuthorizationResult> AuthorizeWithCardAsync(
        Money amount, CardDetails card, string orderReference, string idempotencyKey, CancellationToken ct);

    /// <summary>Creates an order with intent=AUTHORIZE and authorizes it with a previously vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        Money amount, string vaultId, string orderReference, string idempotencyKey, CancellationToken ct);

    /// <summary>Reads the current state of an authorization (to decide idempotency / renewal).</summary>
    Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Renews a stale authorization, returning the fresh authorization to use for capture.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, CancellationToken ct);

    /// <summary>Captures an authorization — actually taking the money — returning PayPal's settlement figures.</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Voids an authorization, releasing the held funds (no money moves).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, Money? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Vaults a card for later reuse, returning its token id and a safe display.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken ct);

    /// <summary>Deletes a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>Lists PayPal's own record of transactions across the whole date range (all pages).</summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
