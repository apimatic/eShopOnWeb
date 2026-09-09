using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. These are passed
/// straight through to PayPal and never persisted in this application.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string Name,
    CardBillingAddress BillingAddress);

public record CardBillingAddress(
    string AddressLine1,
    string AdminArea2, // city
    string AdminArea1, // state / province
    string PostalCode,
    string CountryCode);

/// <summary>Result of creating + authorizing (or re-reading) a hold on PayPal.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>Result of capturing a hold, carrying the figures PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of a refund.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting a card: the reusable id plus a safe descriptor.</summary>
public record VaultCardResult(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry);

/// <summary>A transaction as reported by PayPal's Transaction Search API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? EventCode);
