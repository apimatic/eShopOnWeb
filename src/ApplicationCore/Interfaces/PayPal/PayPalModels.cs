using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details, passed transiently to PayPal. Never persisted, never logged.</summary>
public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of holding funds (an AUTHORIZE-intent order processed against a card or vault token).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record AuthorizationInfo(string Status, DateTimeOffset? ExpiresAt);

public record ReauthorizeResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of taking the money at fulfilment, including what PayPal reported for fee and net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>A card saved to PayPal's vault, described safely (no card data).</summary>
public record VaultedCardResult(
    string PaymentTokenId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string? Name);

public record RefundResult(string RefundId, string Status, decimal Amount, string Currency);

/// <summary>One row of PayPal's own transaction record, used to line up against eShop orders.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string Status,
    decimal Amount,
    string Currency,
    decimal Fee,
    DateTimeOffset Date,
    string? InvoiceId,
    string? CustomField,
    string? Subject,
    string? EventCode);
