using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin, verified wrapper over the PayPal REST APIs this integration needs. Implemented in Infrastructure.
/// All money amounts are decimals in the configured currency; idempotency keys map to PayPal-Request-Id.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Create an AUTHORIZE-intent order for a one-off card payment and place the hold.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithCardAsync(
        decimal amount, string currencyCode, PayPalCardDetails card, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Create an AUTHORIZE-intent order paid by a previously vaulted card, and place the hold.</summary>
    Task<PayPalAuthorizationResult> AuthorizeOrderWithVaultAsync(
        decimal amount, string currencyCode, string vaultId, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Current status of an authorization (CREATED, CAPTURED, VOIDED, EXPIRED, PENDING).</summary>
    Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization; returns the new authorization id.</summary>
    Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Capture an authorization (take the money) at fulfilment.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Void an authorization (release the hold) on cancellation.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vault a raw card without a purchase: create a setup token, returning (setupTokenId, customerId).</summary>
    Task<(string SetupTokenId, string CustomerId)> CreateSetupTokenAsync(
        PayPalCardDetails card, string? customerId, CancellationToken cancellationToken = default);

    /// <summary>Exchange a setup token for a permanent vault payment token (the saved card).</summary>
    Task<PayPalVaultedCard> CreatePaymentTokenAsync(string setupTokenId, CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted payment token so it can no longer be used.</summary>
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// PayPal's own record of transactions across a date range (whole range, all pages, chunked to
    /// respect PayPal's 31-day window). Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
