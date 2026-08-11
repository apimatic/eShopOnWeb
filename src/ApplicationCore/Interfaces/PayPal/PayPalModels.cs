using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or to be vaulted. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry, // YYYY-MM
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>A billing address for a card. Country code is required by PayPal for AVS checks.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// The funding instruction for an authorization: either raw card details for a one-off payment,
/// or the vault token id of a previously saved card. Exactly one must be provided.
/// </summary>
public record PaymentInstrument(CardDetails? Card, string? VaultId)
{
    public static PaymentInstrument FromCard(CardDetails card) => new(card, null);
    public static PaymentInstrument FromVault(string vaultId) => new(null, vaultId);
}

/// <summary>Command to place a hold (authorization) for an order total.</summary>
public record AuthorizeOrderCommand(
    int OrderId,
    decimal Amount,
    string Currency,
    PaymentInstrument Instrument,
    string IdempotencyKey);

/// <summary>The result of a PayPal authorization (hold).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The result of a PayPal capture, carrying the fee and net proceeds PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>The result of a PayPal refund.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>The result of vaulting a card: the token id plus a safe descriptor of the card.</summary>
public record VaultCardResult(
    string VaultId,
    string? Last4,
    string? Brand,
    string? Expiry,
    string? Name);

/// <summary>A PayPal transaction as reported by Transaction Search, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    string? CustomField,
    string? EventCode);
