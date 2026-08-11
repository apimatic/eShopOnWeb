using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. These never touch the
/// application's own database and are never logged — they flow straight to PayPal.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string? Line1,
    string? Line2,
    string? City,      // admin_area_2
    string? State,     // admin_area_1
    string? PostalCode,
    string CountryCode);

/// <summary>
/// A request to place a hold (authorization) for an order total. Exactly one of
/// <see cref="Card"/> or <see cref="VaultId"/> identifies how to pay.
/// </summary>
public record PaymentAuthorizationRequest(
    decimal Amount,
    string Currency,
    string Reference,
    CardDetails? Card,
    string? VaultId,
    string IdempotencyKey);

/// <summary>Result of placing (or renewing) a hold.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>Current state of a hold as PayPal reports it.</summary>
public record AuthorizationInfo(
    string Id,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of taking the money at fulfilment, as PayPal reported it.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of returning money to the shopper.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>A card saved into PayPal's vault, described safely.</summary>
public record VaultedCard(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardType);

/// <summary>A single PayPal reporting transaction, normalised for reconciliation.</summary>
public record GatewayTransaction(
    string TransactionId,
    string? EventCode,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? Date);
