using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or to vault. Never stored or logged by this app.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,           // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

/// <summary>A billing address in PayPal's portable-address shape.</summary>
public sealed record PayPalBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea2 = null,   // city
    string? AdminArea1 = null,   // state
    string? PostalCode = null);

/// <summary>
/// The instrument to fund a payment with: either raw card details (one-off) or the id of a
/// previously vaulted card. Exactly one is set.
/// </summary>
public sealed record PaymentInstrument
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static PaymentInstrument FromCard(CardDetails card) => new() { Card = card };
    public static PaymentInstrument FromVault(string vaultId) => new() { VaultId = vaultId };
}

/// <summary>Result of creating a PayPal checkout order.</summary>
public sealed record CreateOrderResult(string OrderId, string Status);

/// <summary>The state of a PayPal authorization (the hold on the buyer's funds).</summary>
public sealed record AuthorizationResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>The state of a PayPal capture (funds actually taken), with the fee breakdown.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>The state of a PayPal refund, including the running total refunded against the capture.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal GrossAmount,
    decimal? TotalRefunded);

/// <summary>The state of a vaulted card — the token id plus safe descriptors.</summary>
public sealed record VaultCardResult(
    string TokenId,
    string CustomerId,
    string? CardBrand,
    string? CardLast4,
    string? Expiry);

/// <summary>A single transaction as PayPal's Transaction Search reports it, for reconciliation.</summary>
public sealed record PayPalTransaction(
    string? TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
