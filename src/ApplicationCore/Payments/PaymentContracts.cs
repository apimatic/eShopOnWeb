using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off card, or a reference to a saved (vaulted) card.</summary>
public record CardDetails(
    string? Number,
    string? Expiry,          // ISO-8601 YYYY-MM
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,      // state / province
    string? AdminArea2,      // city / town
    string? PostalCode,
    string? CountryCode);    // ISO-3166 alpha-2

/// <summary>
/// A request to authorize (hold) an order total. Exactly one funding source is used: a one-off
/// <see cref="Card"/>, or a <see cref="SavedCardVaultId"/> naming one of the shopper's saved cards.
/// </summary>
public record AuthorizeCardRequest(
    decimal Amount,
    string OrderReference,
    string IdempotencyKey,
    string? Description,
    CardDetails? Card,
    string? SavedCardVaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A request to vault a card for a shopper.</summary>
public record SaveCardRequest(CardDetails Card);

/// <summary>A safe representation of a saved card — never full card details.</summary>
public record SavedCardResult(
    string PaymentMethodId,
    string? CustomerId,
    string? CardBrand,
    string? CardLast4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's reporting knows it.</summary>
public record GatewayTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? InitiatedAt);
