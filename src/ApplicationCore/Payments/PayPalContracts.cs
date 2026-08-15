using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string CardholderName,
    string Number,
    // Expiry in "YYYY-MM" form as PayPal expects.
    string Expiry,
    string SecurityCode,
    string? BillingCountryCode = null,
    string? BillingAddressLine = null,
    string? BillingCity = null,
    string? BillingState = null,
    string? BillingPostalCode = null);

/// <summary>
/// Everything needed to place a hold: the amount/currency, the reconciliation tags, an idempotency
/// key, and exactly one funding source (raw <see cref="Card"/> or a vaulted <see cref="VaultId"/>).
/// </summary>
public record AuthorizeInstruction(
    decimal Amount,
    string CurrencyCode,
    // PayPal custom_id — a stable reference used to line the payment up in reconciliation.
    string CustomId,
    // PayPal invoice_id — a human-readable order reference.
    string InvoiceId,
    Guid IdempotencyKey,
    CardDetails? Card = null,
    string? VaultId = null);

/// <summary>The hold PayPal created: its order id, the authorization id/status and when it expires.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The captured payment as PayPal reports it, including its fee breakdown.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>The result of releasing a hold.</summary>
public record VoidResult(string AuthorizationId, string Status);

/// <summary>The refund PayPal created.</summary>
public record RefundResult(string RefundId, string Status, decimal Amount, string CurrencyCode);

/// <summary>A vaulted card: its vault id and a safe descriptor to show the shopper.</summary>
public record VaultedCardResult(string VaultId, string Brand, string LastDigits, string Expiry);

/// <summary>One PayPal-side transaction from the reporting API, used for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? Date);
