using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off card payment or to vault a card. These are passed straight to PayPal and
/// are NEVER persisted in this application's database and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,   // state / province
    string? AdminArea2 = null,   // city
    string? PostalCode = null);

/// <summary>
/// The source of funds for an authorization: either raw card details for a one-off payment, or the PayPal
/// vault id of a previously saved card. Exactly one is set.
/// </summary>
public record PaymentSourceInput(CardDetails? Card, string? VaultId);

/// <summary>Result of authorizing (holding) an order total.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    bool RequiresBuyerAction,
    string? ActionRel,
    string? ActionHref);

/// <summary>Result of capturing an authorization at fulfilment.</summary>
public record CaptureResult(
    string AuthorizationId,   // the authorization actually captured (may be a renewed one)
    string CaptureId,
    string CaptureStatus,
    bool Pending,
    decimal? Gross,
    decimal? Fee,
    decimal? Net,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a captured payment.</summary>
public record RefundResult(string RefundId, string RefundStatus);

/// <summary>Result of vaulting (saving) a card; carries only a safe descriptor.</summary>
public record VaultCardResult(string VaultId, string CardBrand, string LastFourDigits, string Expiry);

/// <summary>A transaction as PayPal records it, for reconciliation against eShop orders.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? InvoiceId,
    DateTimeOffset? Date);
