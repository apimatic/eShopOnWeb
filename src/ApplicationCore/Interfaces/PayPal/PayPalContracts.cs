using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off (direct) card payment or a vault operation. These are passed
/// straight through to PayPal and are never persisted or logged by this application.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,            // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? City,             // admin_area_2
    string? State,            // admin_area_1
    string? PostalCode,
    string CountryCode);      // ISO-3166 alpha-2, required by PayPal

/// <summary>
/// A request to authorize (hold) an order's total against PayPal. Exactly one of <see cref="Card"/>
/// or <see cref="VaultId"/> is supplied — a one-off card, or a previously saved (vaulted) card.
/// </summary>
public record AuthorizeCardPaymentRequest(
    decimal Amount,
    string Currency,
    string OrderReference,    // eShop order id, stamped onto the PayPal order for reconciliation
    string IdempotencyKey,
    CardDetails? Card,
    string? VaultId);

/// <summary>Result of authorizing an order: the PayPal order id and the resulting hold.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization at fulfilment — what PayPal actually reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of a refund against a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting a card: PayPal's token id and a safe descriptor (never a PAN).</summary>
public record VaultCardResult(
    string VaultId,
    string CardBrand,
    string LastFourDigits,
    string Expiry,
    string? CardholderName);

/// <summary>One PayPal transaction record from the transaction-search (reconciliation) report.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
