using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off payment or a vaulting request. This type is transient — it is
/// passed to the gateway and never persisted or logged. The application's database never stores a PAN.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>Outcome of creating + authorizing a PayPal order (the hold).</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription);

/// <summary>Outcome of capturing an authorization (money taken) with PayPal's own breakdown.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Outcome of renewing a stale authorization.</summary>
public sealed record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Outcome of refunding a captured payment.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A vaulted card: the reusable token plus a safe descriptor. Never carries a PAN.</summary>
public sealed record VaultCardResult(
    string VaultTokenId,
    string Brand,
    string LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal itself records it, for reconciliation.</summary>
public sealed record TransactionRecord(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate);
