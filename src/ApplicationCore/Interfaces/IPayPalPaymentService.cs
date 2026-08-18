using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The seam over PayPal. Every PayPal interaction goes through this interface; the implementation
/// lives in Infrastructure and is the only code that touches the PayPal SDK. Callers deal only in
/// the app's own contract types (above), never in SDK types.
///
/// All write operations take a caller-controlled idempotency key which the implementation forwards
/// to PayPal (PayPal-Request-Id) so a repeated request does not authorize, capture or refund twice.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>
    /// Authorize (hold) <paramref name="amount"/> against the given payment source. Does not capture.
    /// Throws <see cref="Exceptions.PaymentChallengeRequiredException"/> if PayPal needs a browser
    /// approval, and <see cref="Exceptions.PaymentGatewayException"/> on any other PayPal failure.
    /// </summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, PayPalPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Capture a held authorization (take the money) and report the fee and net proceeds.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew a stale authorization before capture. Throws
    /// <see cref="Exceptions.ReauthorizationExpiredException"/> when the hold can no longer be renewed
    /// and an operator must act.
    /// </summary>
    Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void a held authorization (release the funds).</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment. Pass <paramref name="amount"/> null for a full refund, or a value
    /// for a partial refund. Repeating a request under the same <paramref name="idempotencyKey"/> does
    /// not refund twice.
    /// </summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a card for later reuse; returns the vault id and a safe descriptor.</summary>
    Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own record of transactions across the whole date range (walking every page).
    /// A range that PayPal's reporting has not yet caught up to may legitimately return empty.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
