using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Card details supplied for a one-off payment or to vault a card. These values are passed straight
/// through to PayPal and are never persisted in this application's database or written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string? CountryCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string CardBrand,
    string CardLastFour);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record PayPalReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string CardLastFour,
    string? Expiry,
    string? CardHolderName);

/// <summary>A transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);

/// <summary>
/// Abstraction over the PayPal REST API for exactly the capabilities this integration needs.
/// Implementations target the sandbox or live host per configuration. Card details never enter
/// this app's storage; only PayPal-owned ids and statuses come back.
/// </summary>
public interface IPayPalPaymentService
{
    /// <summary>Authorizes (holds) <paramref name="amount"/> using an unvaulted card. Throws
    /// <see cref="Exceptions.PayPalChallengeRequiredException"/> if PayPal requires a browser challenge.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) <paramref name="amount"/> using a previously vaulted card.</summary>
    Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string orderReference, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Captures (takes) a previously authorized payment. Returns the captured amount,
    /// PayPal's fee and the net proceeds.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string invoiceId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Re-authorizes a stale authorization. Throws
    /// <see cref="Exceptions.AuthorizationNotRenewableException"/> when it can no longer be renewed.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default);

    /// <summary>Voids (releases) an authorization that was never captured.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (amount null) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Vaults a card for later reuse, returning its token id and a safe descriptor.</summary>
    Task<PayPalVaultResult> VaultCardAsync(CardDetails card, string? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>Lists PayPal's own transaction records across the whole range (all pages, chunked to
    /// respect PayPal's per-request date-range limit).</summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
