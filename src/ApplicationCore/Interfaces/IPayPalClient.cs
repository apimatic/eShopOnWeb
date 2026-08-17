using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Talks to PayPal's REST API (Orders v2, Payments v2, Vault v3, Reporting v1) on behalf of the
/// application. All money-moving calls carry an idempotency key (PayPal-Request-Id) supplied by
/// the caller so retries never double-charge.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Authorize (hold) the amount using raw card details. Returns the PayPal order id
    /// and authorization id. Throws <see cref="PayPalChallengeRequiredException"/> if PayPal
    /// requires a browser approval.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string reference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Authorize (hold) the amount using a previously vaulted card token.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string vaultTokenId, string reference, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Current status of an authorization (e.g. CREATED, EXPIRED, CAPTURED, VOIDED).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Capture an authorization at fulfilment. If the hold has gone stale it is renewed
    /// (reauthorized) first; if it can no longer be renewed an
    /// <see cref="AuthorizationCannotBeRenewedException"/> is thrown. Returns the capture (with
    /// PayPal's fee/net breakdown) plus whether renewal happened.</summary>
    Task<PayPalCaptureOutcome> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Void an authorization before fulfilment, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Refund a capture in full or in part. The idempotency key makes a repeated request
    /// under the same key return the same refund.</summary>
    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vault a raw card for later reuse. Returns the vault token and a safe description.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default);

    /// <summary>List every PayPal transaction across the whole date range (paginating and
    /// chunking the range into PayPal's 31-day windows).</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
