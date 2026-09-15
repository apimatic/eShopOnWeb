using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Card details for a one-off (direct) card payment or for vaulting. These are passed straight through to
/// PayPal and are NEVER persisted in this app's database or written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,            // "YYYY-MM"
    string SecurityCode,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// Which instrument to charge: either a one-off <see cref="Card"/> or a previously-vaulted card referenced
/// by <see cref="VaultId"/>. Exactly one is set.
/// </summary>
public record CardPaymentInstrument(CardDetails? Card, string? VaultId);

/// <summary>Result of placing a hold (authorize) or renewing one.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current state of an authorization, read back from PayPal.</summary>
public record AuthorizationDetails(string Status, DateTimeOffset? ExpiresAt);

/// <summary>What PayPal reported at capture: the captured amount, PayPal's fee, and net proceeds.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of a refund.</summary>
public record RefundResult(string RefundId, string Status, decimal Amount);

/// <summary>A vaulted card: its token id and a safe descriptor (never full card details).</summary>
public record VaultCardResult(string TokenId, string Brand, string LastFourDigits, string? Expiry);

/// <summary>One transaction from PayPal's transaction search, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    DateTimeOffset? Date);
