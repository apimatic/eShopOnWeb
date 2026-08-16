using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off payment or to vault. These are passed transiently to the
/// payment gateway only; the card number is never stored by this application nor logged.
/// </summary>
public record CardPaymentDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of placing a hold (authorization) on the money.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? CardBrand,
    string? CardLast4,
    string? CardExpiryMonth,
    string? CardExpiryYear);

/// <summary>Result of taking the money (capture), including what PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>Result of a refund against a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>A card saved (vaulted) with PayPal, described safely.</summary>
public record VaultCardResult(
    string VaultId,
    string Brand,
    string Last4,
    string ExpiryMonth,
    string ExpiryYear);

/// <summary>One transaction as PayPal itself records it, for reconciliation.</summary>
public record GatewayTransaction(
    string TransactionId,
    string EventCode,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date);
