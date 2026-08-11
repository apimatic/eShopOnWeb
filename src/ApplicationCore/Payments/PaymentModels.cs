using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Transient models exchanged with the PayPal integration. None of these are persisted verbatim;
/// in particular <see cref="CardDetails"/> is never stored in the application database or logged.
/// </summary>
public record Money(decimal Amount, string Currency);

/// <summary>Raw card details supplied for a one-off payment or to be vaulted. Never persisted, never logged.</summary>
public record CardDetails(
    string Number,
    string Expiry, // YYYY-MM as required by PayPal
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Card billing address (PayPal field names implied via mapping in the client).</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,        // admin_area_2
    string? State,       // admin_area_1
    string? PostalCode,
    string? CountryCode);

/// <summary>A safe, non-sensitive description of a card (never contains the PAN or CVC).</summary>
public record CardSummary(string Brand, string LastDigits, string Expiry, string? CardholderName);

/// <summary>Result of creating an authorized PayPal order (a hold on the money).</summary>
public record AuthorizationOutcome(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    CardSummary? Card);

/// <summary>Current PayPal-side state of an authorization.</summary>
public record AuthorizationSnapshot(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, carrying the fee breakdown PayPal reports.</summary>
public record CaptureOutcome(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

/// <summary>Result of refunding a capture (full or partial).</summary>
public record RefundOutcome(string RefundId, string Status, decimal Amount, string Currency);

/// <summary>Result of vaulting (saving) a card at PayPal.</summary>
public record VaultedCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry,
    string? CardholderName);

/// <summary>A single transaction as reported by PayPal's transaction-search (reconciliation) API.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string? Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? Date,
    string? EventCode,
    string? CustomField,
    string? InvoiceId,
    decimal? FeeAmount);
