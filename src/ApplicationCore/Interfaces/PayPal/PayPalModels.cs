using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off card payment or for vaulting. These values are passed
/// straight to PayPal and are never persisted in the application's database or written to logs.
/// </summary>
public sealed record PayPalCardDetails(
    string Number,
    string Expiry,          // Internet date format YYYY-MM, e.g. "2030-04"
    string SecurityCode,
    string? Name,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string CountryCode);    // 2-char ISO 3166-1

/// <summary>
/// A request to place a hold (authorization) on the order total. Exactly one funding source is
/// used: either raw <see cref="Card"/> details for a one-off payment, or a saved-card
/// <see cref="VaultTokenId"/>.
/// </summary>
public sealed record PayPalAuthorizationRequest(
    decimal Amount,
    string InvoiceId,
    string RequestId,
    PayPalCardDetails? Card,
    string? VaultTokenId,
    string? Description);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    string? CardBrand,
    string? CardLast4);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public sealed record PayPalVaultedCardResult(
    string VaultTokenId,
    string CustomerId,
    string? CardBrand,
    string? CardLast4,
    string? CardExpiry);

/// <summary>A single transaction as PayPal's own reporting reports it, for reconciliation.</summary>
public sealed record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate,
    string? EventCode);
