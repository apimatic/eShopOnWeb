using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// Raw card details for a one-off card payment or for vaulting. This is a transient input only —
/// it is never persisted in the application's database and never written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,            // "YYYY-MM"
    string? SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingCity,      // admin_area_2
    string? BillingState,     // admin_area_1
    string? BillingPostalCode,
    string? BillingCountryCode);

/// <summary>The outcome of creating a PayPal order and authorizing it (placing a hold on the funds).</summary>
public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string AuthorizationStatus);

/// <summary>A single PayPal authorization's id and current status.</summary>
public record AuthorizationDetails(string AuthorizationId, string Status);

/// <summary>What PayPal reported for a capture: the taken amount, PayPal's fee, and the net to the merchant.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>The id and status of a refund PayPal has issued.</summary>
public record RefundResult(string RefundId, string Status);

/// <summary>A vaulted card: the token that stands in for the card plus its safe description.</summary>
public record VaultedCard(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's own reporting records it, for reconciliation.</summary>
public record GatewayTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset InitiationDate,
    string? EventCode);
