using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Card details for a one-off (unvaulted) payment or a vault request. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth, // ISO-8601 "YYYY-MM"
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AdminArea2, // city
    string? AdminArea1, // state / province
    string? PostalCode,
    string? CountryCode); // ISO 3166-1 alpha-2

/// <summary>Input to authorize an order total — either a one-off card or a saved (vaulted) card.</summary>
public record AuthorizePaymentRequest(
    int OrderId,
    decimal Amount,
    string Currency,
    string InvoiceId,
    string CustomId,
    string? Description,
    CardDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal AuthorizedAmount,
    string Currency,
    string? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    string? ExpiresAt);

public record AuthorizationStatusResult(
    string Status,
    string? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting a card. Carries only safe descriptors — never the card number.</summary>
public record VaultCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFourDigits,
    string ExpiryMonthYear,
    string? CardholderName);

public record VaultCardRequest(
    CardDetails Card,
    string MerchantCustomerId,
    string? ExistingCustomerId);

/// <summary>One PayPal transaction from the reporting API, for reconciliation against eShop orders.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? Currency,
    string? Status,
    string? InitiationDate);

/// <summary>All transactions PayPal reports for a range, plus whether the walk was truncated.</summary>
public record ReconciliationSearchResult(
    IReadOnlyList<ReconciliationTransaction> Transactions,
    int PagesScanned,
    int TotalItems,
    bool Truncated);

/// <summary>A capture re-read from PayPal after an ambiguous (transport-failed) capture request.</summary>
public record CaptureLookupResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);
