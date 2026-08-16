using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// ---- Gateway data-transfer records (provider-agnostic shapes the domain works with) ----

public record PaymentBillingAddress(
    string? Line1, string? Line2, string? City, string? State, string? PostalCode, string? CountryCode);

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by the app.</summary>
public record PaymentCard(
    string Number, int ExpiryMonth, int ExpiryYear, string SecurityCode,
    string? CardholderName, PaymentBillingAddress? BillingAddress);

public record PaymentOrderLine(string Name, int Quantity, decimal UnitPrice);

/// <summary>
/// A request to place a hold (authorization) at PayPal. Exactly one of <see cref="Card"/> or
/// <see cref="VaultId"/> is supplied.
/// </summary>
public record AuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string ReconciliationReference,
    IReadOnlyList<PaymentOrderLine> Lines,
    PaymentCard? Card,
    string? VaultId,
    string IdempotencyKey);

public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId, string Status, decimal GrossAmount, decimal PayPalFee, decimal NetAmount,
    string CurrencyCode, DateTimeOffset? CapturedAt);

public record RefundResult(string RefundId, string Status, decimal Amount, string CurrencyCode);

public record VaultResult(string VaultId, string? Brand, string? LastFourDigits, string? Expiry, string? CardholderName);

public record TransactionRecord(
    string TransactionId, string Status, decimal Amount, string CurrencyCode, decimal Fee,
    string? InvoiceId, string? CustomField, DateTimeOffset? Date, string? Subject, string? EventCode);

/// <summary>
/// Abstraction over the PayPal REST API. Implemented in Infrastructure; the sole way the
/// application talks to PayPal.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Places a hold (AUTHORIZE) for the order total using a raw card or a vaulted card.</summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously placed authorization (takes the money).</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Renews a stale authorization, returning fresh hold details.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the current state of an authorization, or null if PayPal has no record of it.</summary>
    Task<AuthorizationResult?> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Voids an authorization, releasing the held funds.</summary>
    Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a capture, in full (null amount) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, string? invoiceId, string? noteToPayer, CancellationToken cancellationToken = default);

    /// <summary>Stores a card in PayPal's vault and returns a reusable token plus a safe descriptor.</summary>
    Task<VaultResult> VaultCardAsync(PaymentCard card, string buyerReference, CancellationToken cancellationToken = default);

    /// <summary>Removes a card from PayPal's vault so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists PayPal's own transaction records over a date range. The implementation pages through
    /// the entire range (and chunks ranges longer than PayPal's per-request limit).
    /// </summary>
    Task<IReadOnlyList<TransactionRecord>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
