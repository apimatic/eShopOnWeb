using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Portable billing address for a card, shaped after PayPal's address_portable model.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// A request to authorize (place a hold for) an order total. Exactly one of <see cref="Card"/>
/// or <see cref="VaultId"/> identifies the funding source.
/// </summary>
public record AuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string CustomId,
    string? Description,
    CardDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
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
    string CardBrand,
    string LastDigits,
    string? ExpiryYearMonth,
    string? CardholderName);

/// <summary>One transaction as PayPal's own reporting knows it, used for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? InvoiceId,
    string? Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? Date,
    decimal? FeeAmount,
    string? EventCode);
