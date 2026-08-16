using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port to the payment processor (PayPal). The application core depends only on this
/// abstraction; the concrete PayPal REST integration lives in the Infrastructure layer.
/// Every method maps to a real PayPal Orders-v2 / Payments-v2 / Vault-v3 / Reporting call.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorize (place a hold on) the order total. Creates a PayPal order with intent
    /// AUTHORIZE using either an inline card or a saved (vaulted) card, and returns the
    /// resulting authorization. The money is held, not taken.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Capture a previously created authorization — this is when money actually moves.</summary>
    Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Renew a stale authorization so it can still be captured.</summary>
    Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Void an authorization, releasing the held funds. No money moves.</summary>
    Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a capture's current state, including the fee/net breakdown PayPal reports.</summary>
    Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default);

    /// <summary>Refund a capture, in full (amount null) or in part.</summary>
    Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string requestId, string? invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card in PayPal's PCI-compliant store for later reuse.</summary>
    Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a vaulted card so it can no longer be charged.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List PayPal's own transaction records for a date range, transparently chunking the range
    /// into PayPal's 31-day windows and following pagination so the whole range is covered.
    /// </summary>
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Billing address for a card, in PayPal's field vocabulary.</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state / province
    string? PostalCode,
    string? CountryCode);

/// <summary>Raw card details for a one-off payment or vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string? Name,
    BillingAddress? BillingAddress);

/// <summary>Request to authorize an order total against a card or a saved card.</summary>
public record AuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string CustomId,   // eShop order id, for reconciliation
    string InvoiceId,  // unique merchant reference, for reconciliation
    string RequestId,  // idempotency key (PayPal-Request-Id)
    CardDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? CardBrand,
    string? CardLast4);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string? Expiry);

/// <summary>A single PayPal transaction as reported by the reporting API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? Status,
    decimal Amount,
    decimal? Fee,
    string CurrencyCode,
    string? CustomField,
    string? InvoiceId,
    string? EventCode,
    DateTimeOffset? Date);
