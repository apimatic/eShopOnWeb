using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The gateway to PayPal's REST API for the capabilities this integration needs. Every method
/// targets the sandbox (or the configured environment/base url). Idempotency keys are passed
/// through to PayPal's PayPal-Request-Id header so a double-click never charges twice.
/// </summary>
public interface IPayPalClient
{
    /// <summary>
    /// Create a PayPal order with intent=AUTHORIZE paid by a raw card, placing a hold for
    /// <paramref name="amount"/>. Returns the resulting authorization. Throws
    /// <c>PaymentChallengeRequiredException</c> if PayPal requires a browser approval (3DS).
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderWithCardAsync(
        decimal amount, string currency, CardDetails card, string customId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Same as <see cref="AuthorizeOrderWithCardAsync"/> but paying with a vaulted (saved) card token.</summary>
    Task<AuthorizationResult> AuthorizeOrderWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string customId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Capture an authorization for the given amount (takes the money).</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Reauthorize a stale authorization, yielding a fresh authorization id to capture.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken ct = default);

    /// <summary>Void an authorization, releasing the held funds (cancel before capture).</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Refund a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault (save) a card, returning its token id and safe display details.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted card token so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// List every PayPal-recorded transaction in the date range, following pagination across the
    /// whole range (not just the first page).
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
