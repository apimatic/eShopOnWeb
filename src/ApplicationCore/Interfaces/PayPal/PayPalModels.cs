using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off payment or to vault a card. These flow straight through to PayPal and
/// are never persisted or logged by this app.
/// </summary>
public record CardPaymentDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    string? CardholderName = null,
    CardBillingAddress? BillingAddress = null)
{
    /// <summary>Expiry formatted as PayPal expects it (YYYY-MM).</summary>
    public string ExpiryYearMonth => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
}

/// <summary>Optional billing address for a card.</summary>
public record CardBillingAddress(
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,   // state / province
    string? AdminArea2 = null,   // city
    string? PostalCode = null,
    string? CountryCode = null);

/// <summary>Result of placing a hold (authorization) on an order total.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, including what PayPal reported for fee/net.</summary>
public record CaptureResult(
    string CaptureId,
    string? Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of renewing (reauthorizing) a stale hold.</summary>
public record ReauthorizationResult(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string? Status,
    decimal? Amount,
    decimal? TotalRefunded);

/// <summary>A vaulted card: the token used to reference it later plus a safe description.</summary>
public record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? InitiationDate);
