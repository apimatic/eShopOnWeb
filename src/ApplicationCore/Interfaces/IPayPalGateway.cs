using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary to PayPal. Everything below the money movement — creating a checkout
/// order, placing/renewing/releasing a hold, capturing, refunding, vaulting cards and reading the
/// merchant's transaction ledger — goes through this one seam. Implemented in Infrastructure over
/// the PayPal SDK; kept SDK-free here so the domain and tests never depend on the SDK.
///
/// The currency is fixed by configuration on the implementation, so callers pass plain decimal
/// amounts (from catalog prices) and the gateway echoes the currency it used back in each result.
/// Implementations translate PayPal failures into the ApplicationCore payment exceptions.
/// </summary>
public interface IPayPalGateway
{
    /// <summary>
    /// Authorize (place a hold on) the given amount. Creates a PayPal order with AUTHORIZE intent
    /// paid by the supplied one-off card or vaulted card, then authorizes it. Does not take the
    /// money. Throws <see cref="Exceptions.PaymentChallengeRequiredException"/> if PayPal demands a
    /// browser approval, and <see cref="Exceptions.PayPalGatewayException"/> on decline/error.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew a stale/expired hold before capture, for the given amount. Returns the fresh
    /// authorization. Throws <see cref="Exceptions.PayPalGatewayException"/> if it can no longer be
    /// renewed (the caller turns that into an operator-actionable failure).
    /// </summary>
    Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture (take the money on) an existing authorization. <paramref name="requestId"/> is the
    /// PayPal-Request-Id idempotency key so a retry never captures twice.
    /// </summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Void (release) an authorization so the held funds return to the shopper.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment, in full (<paramref name="amount"/> null) or in part.
    /// <paramref name="idempotencyKey"/> is the caller-supplied PayPal-Request-Id so repeating a
    /// request under the same key does not refund twice.
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Save (vault) a card for later reuse. Returns the vault token and a safe descriptor.</summary>
    Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardInput card, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions for a date range, paged through in full. Used by the
    /// reconciliation report to line PayPal's ledger up against eShop orders.
    /// </summary>
    Task<IReadOnlyList<PayPalLedgerEntry>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
