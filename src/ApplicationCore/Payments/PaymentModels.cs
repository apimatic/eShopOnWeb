using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Transient raw card details supplied for a one-off payment or to be vaulted. NEVER persisted in
/// the application's own database and NEVER written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth, // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Billing address for a card, in PayPal's address vocabulary.</summary>
public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,   // city / town
    string? AdminArea1,  // state / province
    string PostalCode,
    string CountryCode); // ISO 3166-1 alpha-2

/// <summary>Result of creating a hold on the money.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status);

/// <summary>Result of capturing an authorization, including what PayPal reported financially.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>Result of a refund against a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting a card: the token plus a safe descriptor to recognise the card.</summary>
public record VaultResult(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry);

/// <summary>A single PayPal transaction record for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    string? EventCode);
