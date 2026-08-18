using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. These never touch the application's
/// own database and must never be logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,        // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    BillingAddress BillingAddress);

/// <summary>Billing address for a card. <see cref="CountryCode"/> is a 2-letter ISO-3166 code.</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>Result of authorizing (placing a hold for) an order total.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current state of an authorization, used to detect staleness before capture.</summary>
public record AuthorizationState(
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of renewing a stale authorization.</summary>
public record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Result of capturing an authorization. <see cref="PayPalFee"/> / <see cref="NetAmount"/> may be null when
/// PayPal did not return a seller-receivable breakdown.
/// </summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>Result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting a card; carries only a safe description, never the PAN.</summary>
public record VaultedCardResult(
    string VaultId,
    string CardBrand,
    string LastFourDigits,
    string Expiry);

/// <summary>A single PayPal-reported transaction, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiatedAt);
