using System;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,            // "YYYY-MM"
    string? SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,       // city
    string? AdminArea1,       // state / province
    string? PostalCode,
    string? CountryCode)
{
    public string LastFourDigits =>
        new string((Number ?? string.Empty).Where(char.IsDigit).ToArray()) is { Length: >= 4 } d
            ? d[^4..]
            : "0000";
}

/// <summary>Result of creating + authorizing a PayPal order (placing the hold).</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    string OrderStatus);

/// <summary>Result of capturing an authorization, including what PayPal reported it kept.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>Result of a refund against a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card that has been vaulted (saved) at PayPal — safe fields only.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string CustomerId,
    string Brand,
    string LastFourDigits,
    string Expiry);

/// <summary>A single transaction as PayPal's reporting knows it, used for reconciliation.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? FeeAmount,
    string? InvoiceId,
    DateTimeOffset? InitiationDate);
