using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or a card being vaulted. This type is
/// transient: it is never persisted to the application database and never logged.
/// </summary>
public sealed record CardPaymentDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    string CardholderName,
    string BillingAddressLine1,
    string? BillingAddressLine2,
    string BillingCity,
    string? BillingState,
    string BillingPostalCode,
    string BillingCountryCode)
{
    /// <summary>Expiry formatted as PayPal expects it: YYYY-MM.</summary>
    public string ExpiryYyyyMm() => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";

    /// <summary>Last four digits, safe to display/store.</summary>
    public string Last4() => Number.Length >= 4 ? Number[^4..] : Number;
}

/// <summary>Outcome of authorizing (holding) an order total at PayPal.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string Currency,
    string CardBrand,
    string CardLast4);

/// <summary>Outcome of capturing an authorization, including PayPal's fee breakdown.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>Outcome of a fresh reauthorization of a stale hold.</summary>
public sealed record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Outcome of refunding a capture, in full or in part.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A card that has been vaulted at PayPal for reuse.</summary>
public sealed record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string CardLast4,
    string CardExpiry);

/// <summary>A single transaction as reported by PayPal's transaction search.</summary>
public sealed record GatewayTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    DateTimeOffset Date,
    string? EventCode,
    string? InvoiceId);
