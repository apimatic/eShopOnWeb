using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card details for a one-off payment or to vault. This is a transient value carried through
/// the request only; it is never persisted in the application database and never logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,           // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    BillingAddress BillingAddress);

/// <summary>Minimal billing address required by PayPal for card processing.</summary>
public sealed record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,      // city
    string? AdminArea1,      // state / province
    string? PostalCode,
    string CountryCode);     // ISO 3166-1 alpha-2

/// <summary>
/// How to fund a payment: either raw card details (one-off) or a reference to a saved (vaulted) card.
/// Exactly one is populated.
/// </summary>
public sealed record PaymentInstrument
{
    private PaymentInstrument() { }

    public CardDetails? Card { get; private init; }
    public string? VaultId { get; private init; }

    public bool IsVaulted => VaultId is not null;

    public static PaymentInstrument FromCard(CardDetails card) => new() { Card = card };
    public static PaymentInstrument FromVault(string vaultId) => new() { VaultId = vaultId };
}

/// <summary>The result of authorizing (or re-authorizing) an order total with PayPal.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The result of capturing an authorization, including what PayPal reported it took.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

/// <summary>The result of refunding a capture.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>The result of vaulting (saving) a card. Carries only safe, recognisable metadata.</summary>
public sealed record VaultResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as reported by PayPal's Transaction Search, for reconciliation.</summary>
public sealed record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
