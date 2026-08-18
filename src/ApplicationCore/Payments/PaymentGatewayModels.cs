using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details supplied for a one-off payment or to vault a card. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Optional billing address for AVS. CountryCode is the only value PayPal requires when present.</summary>
public record CardBillingAddress(
    string CountryCode,     // 2-letter ISO
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode);

/// <summary>How an order is to be paid: either a raw one-off card, or a reference to a saved (vaulted) card.</summary>
public record PaymentInstrument(CardDetails? Card, int? SavedCardId);

/// <summary>Result of authorizing (placing a hold on) an order total.</summary>
public record PayPalAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization at fulfilment, including what PayPal reported.</summary>
public record PayPalCapture(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of refunding a captured payment (full or partial).</summary>
public record PayPalRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting a card: the vault id plus safe display data (never full PAN).</summary>
public record PayPalVaultedCard(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry);

/// <summary>A single PayPal ledger transaction as reported by transaction search, for reconciliation.</summary>
public record PayPalTransaction(
    string? TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);
