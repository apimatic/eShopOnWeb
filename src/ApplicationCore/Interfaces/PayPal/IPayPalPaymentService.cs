using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// The application's boundary to PayPal. Every method translates PayPal SDK types and failures into
/// the plain domain contracts above (or a domain payment exception), so no SDK type leaks past the
/// infrastructure layer.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>Authorize (hold) an order total. Does not capture. Idempotent on the request key.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardPaymentRequest request, CancellationToken ct);

    /// <summary>
    /// Capture a held authorization (fulfilment). Throws
    /// <see cref="Exceptions.PaymentAuthorizationExpiredException"/> when the hold has gone stale and
    /// must be renewed first.
    /// </summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Renew a stale authorization (re-authorize) so it can be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Void a held authorization (cancel before fulfilment) — releases the funds.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Read the captured amount straight from PayPal, to cap refunds at what was captured.</summary>
    Task<decimal> GetCapturedAmountAsync(string captureId, CancellationToken ct);

    /// <summary>Refund a capture, full (<paramref name="amount"/> null) or partial.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct);

    /// <summary>Vault a card without taking a payment; returns the token id + a safe descriptor.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>
    /// PayPal's own transaction records over a date range, across the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}
