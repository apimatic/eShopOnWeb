using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A monetary amount with its ISO-4217 currency. Value is carried to the cent.</summary>
public record PaymentAmount(decimal Value, string Currency);

/// <summary>Raw card details used for a one-off payment or to vault a card. Never persisted or logged.</summary>
public record CardPaymentDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Optional billing address for a card.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>The hold placed with PayPal.</summary>
public record PayPalAuthorization(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of creating + authorizing a PayPal order.</summary>
public record AuthorizeResult(string PayPalOrderId, PayPalAuthorization Authorization);

/// <summary>The captured payment and PayPal's reported fee breakdown.</summary>
public record PayPalCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>A refund issued against a capture.</summary>
public record PayPalRefund(string RefundId, string Status, decimal Amount, string Currency);

/// <summary>A vaulted card: the token used to pay later plus a safe descriptor.</summary>
public record VaultedCard(
    string VaultId,
    string? Brand,
    string LastFourDigits,
    string? ExpiryMonth,
    string? ExpiryYear);

/// <summary>
/// One transaction as PayPal's reporting knows it, with the eShop reference (custom/invoice id)
/// so it can be lined up against an eShop order.
/// </summary>
public record GatewayTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? FeeAmount,
    DateTimeOffset? InitiationDate,
    string? ReferenceId,
    string? EventCode);
