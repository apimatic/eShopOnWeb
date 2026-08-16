using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details supplied for a one-off payment or to be vaulted. Never persisted or logged by this app.</summary>
public record CardDetails(
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

/// <summary>Outcome of authorizing (placing a hold). Carries the PayPal ids/status a later request needs.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription);

/// <summary>Outcome of a capture, reflecting what PayPal reported: gross taken, fee, and net proceeds.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Safe display metadata for a vaulted card. No full card data.</summary>
public record PayPalVaultResult(
    string VaultId,
    string CardBrand,
    string Last4,
    string? ExpiryMonth,
    string? ExpiryYear);

/// <summary>A single transaction from PayPal's transaction-search report, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    decimal Amount,
    string CurrencyCode,
    string Status,
    DateTimeOffset Date);
