using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>Raw card details for a one-off card payment or to save a new card. Never persisted.</summary>
public record CardDetails(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    string AddressLine1,
    string City,
    string PostalCode,
    string CountryCode);

/// <summary>
/// A request to authorize (hold, not capture) a payment. Exactly one of <see cref="Card"/> or
/// <see cref="VaultId"/> must be supplied.
/// </summary>
public record AuthorizePaymentRequest(
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record AuthorizationFreshnessResult(bool IsFresh, string Status, DateTimeOffset? ExpiresAt);

public record ReauthorizationResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(string CaptureId, string Status, decimal GrossAmount, decimal FeeAmount, decimal NetAmount);

public record RefundResult(string RefundId, string Status, decimal Amount);

public record SavedCardResult(string VaultId, string CardBrand, string LastDigits, string Expiry);

public record PayPalTransaction(
    string? TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiationDate);
