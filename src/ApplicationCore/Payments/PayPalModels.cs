using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A monetary amount with its ISO currency code.</summary>
public record Money(string CurrencyCode, decimal Value);

/// <summary>A billing address for a card. All fields optional; passed through to PayPal.</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// Raw card details for a one-off payment or to vault. These are transient: never persisted in the
/// application database and never written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

/// <summary>The result of authorizing (placing a hold on) an order total.</summary>
public record AuthorizeResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastDigits,
    string? CardExpiry);

/// <summary>The result of capturing an authorization, including what PayPal reported for fee and net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>The result of renewing a stale authorization.</summary>
public record ReauthorizeResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>The result of refunding a capture.</summary>
public record RefundResult(string RefundId, string Status, decimal Amount);

/// <summary>A saved (vaulted) card as PayPal describes it back to us — safe descriptor only.</summary>
public record VaultedCardResult(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

/// <summary>One transaction from PayPal's own reporting, used for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal Fee,
    DateTimeOffset Date,
    string? EventCode);
