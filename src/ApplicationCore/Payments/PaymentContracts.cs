using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or to vault a card. These never leave the gateway boundary,
/// are never persisted by this application, and are never logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,     // state / province
    string? AdminArea2,     // city
    string? PostalCode,
    string? CountryCode);   // ISO-3166-1 alpha-2

/// <summary>
/// Instruction for how to pay: exactly one of a one-off <see cref="Card"/> or a saved-card
/// <see cref="VaultId"/>.
/// </summary>
public sealed record PaymentInstrument(CardDetails? Card, string? VaultId);

/// <summary>Result of authorizing an order total (placing a hold).</summary>
public sealed record AuthorizationOutcome(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? PaymentMethodDescription);

/// <summary>Result of capturing an authorized payment, with what PayPal reported it netted.</summary>
public sealed record CaptureOutcome(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>Result of refunding a captured payment.</summary>
public sealed record RefundOutcome(
    string PayPalRefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting a card — safe display details plus the vault-token id.</summary>
public sealed record VaultCardOutcome(
    string VaultId,
    string Brand,
    string Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One PayPal transaction as reported by PayPal, for reconciliation against eShop orders.</summary>
public sealed record ReconciliationTransaction(
    string? TransactionId,
    string? PayPalReferenceId,
    string? InvoiceId,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiationDate);
