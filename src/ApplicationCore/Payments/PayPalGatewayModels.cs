using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A card's billing address (maps to the PayPal card billing_address schema).</summary>
public record CardBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These are passed straight to
/// PayPal and are never persisted by this application.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>
/// Request to authorize an order total. Exactly one of <see cref="Card"/> (one-off) or
/// <see cref="VaultId"/> (a saved card) is supplied.
/// </summary>
public record AuthorizeOrderRequest(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string CustomId,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId);

public record AuthorizeOrderResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultCardRequest(
    CardDetails Card,
    string MerchantCustomerId,
    string IdempotencyKey);

public record VaultCardResult(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry);

/// <summary>A single transaction from PayPal's transaction reporting, trimmed to the fields we reconcile on.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? EventCode,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    string? Status);
