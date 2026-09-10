using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or for saving a card. Held transiently only — the application
/// never persists the number or security code and never writes them to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Optional billing address for a card.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,     // state / province
    string? AdminArea2,     // city
    string? PostalCode,
    string? CountryCode);   // 2-letter ISO-3166

/// <summary>Result of authorizing (holding) funds for an order.</summary>
public record AuthorizationOutcome(string PayPalOrderId, string AuthorizationId, string? ExpiresAt, string Status);

/// <summary>Result of capturing (taking) an authorized payment, with PayPal's reported breakdown.</summary>
public record CaptureOutcome(string CaptureId, decimal Amount, decimal? Fee, decimal? Net, string Status);

/// <summary>Result of re-authorizing a stale hold.</summary>
public record ReauthorizeOutcome(string AuthorizationId, string? ExpiresAt, string Status);

/// <summary>Result of refunding a captured payment, in full or in part.</summary>
public record RefundOutcome(string RefundId, decimal Amount, string Status);

/// <summary>A card saved in the PayPal vault, described safely enough to recognise it.</summary>
public record VaultedCard(string VaultId, string? CustomerId, string Brand, string LastDigits, string? Expiry, string? CardholderName);

/// <summary>One transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? InitiationDate,
    decimal? FeeAmount);
