using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Billing address for card AVS. All parts optional; sensible defaults are applied.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>The result of placing (or renewing) a hold with PayPal.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

/// <summary>The current state of an authorization as read back from PayPal.</summary>
public record PayPalAuthorizationDetails(string Status, DateTimeOffset? ExpiresAt);

/// <summary>The result of capturing a hold, including PayPal's fee and net proceeds.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>The result of refunding a capture.</summary>
public record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency);

/// <summary>A safe summary of a card that has been vaulted.</summary>
public record VaultedCard(
    string VaultId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry);

/// <summary>One row of PayPal's transaction report, used for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    string EventCode,
    decimal Amount,
    decimal Fee,
    string Currency,
    DateTimeOffset Date);
