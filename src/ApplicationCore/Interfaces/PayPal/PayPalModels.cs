using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. Held only in memory for the
/// duration of a single PayPal call — never persisted to this application's database and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,        // YYYY-MM
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,   // city
    string? AdminArea1,   // state / province
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of authorizing an order total (placing a hold) with PayPal.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>Result of renewing (reauthorizing) a stale hold.</summary>
public record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization — what PayPal reported was taken.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting (saving) a card. Carries only safe, displayable descriptors.</summary>
public record VaultResult(
    string VaultId,
    string CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string CardholderName);

/// <summary>The current state of an authorization as PayPal sees it.</summary>
public record AuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>One row of PayPal's own transaction record, used to reconcile against eShop orders.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    string? EventCode,
    decimal Amount,
    decimal Fee,
    string Currency,
    DateTimeOffset InitiationDate,
    string? InvoiceId,
    string? CustomField);
