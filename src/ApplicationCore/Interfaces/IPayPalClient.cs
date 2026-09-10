using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal REST API. Implemented in Infrastructure against the PayPal
/// sandbox/live endpoints; kept here so ApplicationCore has no dependency on Infrastructure.
/// </summary>
public interface IPayPalClient
{
    /// <summary>The configured settlement currency (from PayPal:Currency).</summary>
    string Currency { get; }

    /// <summary>
    /// Creates a PayPal order with intent AUTHORIZE paying with raw card details, placing a
    /// hold on the funds without capturing. <paramref name="idempotencyKey"/> makes a
    /// repeated call safe. Throws <see cref="Exceptions.PayPalChallengeRequiredException"/>
    /// if PayPal requires the shopper to approve in a browser.
    /// </summary>
    Task<AuthorizationResult> AuthorizeOrderWithCardAsync(decimal amount, string currency,
        string invoiceId, CardDetails card, string idempotencyKey, CancellationToken ct = default);

    /// <summary>As above, but paying with a previously vaulted card (payment-method token).</summary>
    Task<AuthorizationResult> AuthorizeOrderWithVaultAsync(decimal amount, string currency,
        string invoiceId, string vaultId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Captures an authorization (takes the held money).</summary>
    Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Voids (cancels) an authorization, releasing the held funds.</summary>
    Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>Reauthorizes a stale authorization, returning a new authorization id.</summary>
    Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct = default);

    /// <summary>Returns the current status of an authorization (CREATED, CAPTURED, VOIDED, EXPIRED, ...).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct = default);

    /// <summary>
    /// Refunds a capture, fully (null amount) or partially. <paramref name="idempotencyKey"/>
    /// is the caller-supplied key that prevents a repeat from refunding twice.
    /// </summary>
    Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>Vaults a card without a purchase, returning a reusable payment-method token.</summary>
    Task<VaultedCardResult> VaultCardAsync(CardDetails card, CancellationToken ct = default);

    /// <summary>Best-effort deletion of a vaulted payment-method token.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Lists PayPal's own record of transactions over a date range, transparently splitting
    /// the range into PayPal's maximum 31-day windows and following pagination so the whole
    /// range is covered, not just the first page.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default);
}
