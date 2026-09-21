using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over PayPal payment operations. The implementation (in Infrastructure) is the only
/// code that touches the PayPal SDK; ApplicationCore depends solely on these plain types so the
/// domain and application services stay provider-agnostic and testable.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>The configured ISO-4217 currency for all amounts.</summary>
    string CurrencyCode { get; }

    /// <summary>Create a PayPal order (intent=AUTHORIZE) and authorize it — a hold, not a capture.</summary>
    Task<PayPalAuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct);

    /// <summary>Capture (take) an authorized payment. A null amount captures the full authorization.</summary>
    Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Re-authorize a stale hold to renew it before capture.</summary>
    Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct);

    /// <summary>Void (release) an authorization before capture.</summary>
    Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Refund a captured payment in full (null amount) or in part.</summary>
    Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, string? noteToPayer, string? customId, CancellationToken ct);

    /// <summary>Vault a card for reuse and return its safe description.</summary>
    Task<PayPalVaultedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct);

    /// <summary>Remove a vaulted card so it can no longer be used to pay.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct);

    /// <summary>List PayPal's own transaction records across a date range (all pages).</summary>
    Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Raw card details for a one-off payment or a vault. Never persisted by the application.</summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Billing address for a card. Country code is required by PayPal.</summary>
public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? PostalCode = null);

/// <summary>Request to authorize an order total against a card or a saved (vaulted) card.</summary>
public record AuthorizeCardRequest(
    decimal Amount,
    string OrderReference,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId,
    string? PayPalCustomerId);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

public record PayPalCaptureResult(
    string CaptureId,
    string? Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record PayPalReauthorizeResult(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

public record PayPalRefundResult(
    string RefundId,
    string? Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Request to vault a card for a shopper.</summary>
public record VaultCardRequest(
    CardDetails Card,
    string? PayPalCustomerId,
    string IdempotencyKey);

public record PayPalVaultedCardResult(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>A PayPal transaction as reported by Transaction Search, for reconciliation.</summary>
public record PayPalTransactionRecord(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    decimal? Fee,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? Date,
    string? EventCode);
