using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public record PayPalCard(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? Line1,
    string? Line2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of creating a PayPal order and authorizing it (placing the hold).</summary>
public record PayPalAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription);

/// <summary>Current state of an authorization as PayPal reports it.</summary>
public record PayPalAuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, with the money PayPal actually reported.</summary>
public record PayPalCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A card saved into PayPal's vault. Carries only a safe descriptor of the card.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFour,
    string Expiry,
    string? Name);

/// <summary>One line of PayPal's own transaction record, used for reconciliation.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string EventCode,
    string Status,
    decimal Amount,
    string Currency,
    decimal? FeeAmount,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId);
