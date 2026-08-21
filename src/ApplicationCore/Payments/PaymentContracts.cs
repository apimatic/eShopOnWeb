using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Transient card details supplied for a one-off payment or to vault a card.
/// These values are NEVER persisted in the application database and NEVER written to logs.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Billing address for a card, mapped onto the PayPal address model by the gateway.</summary>
public sealed record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// How a payment is funded: either raw card details for a one-off payment, or the id of a
/// previously vaulted (saved) card. Exactly one must be supplied.
/// </summary>
public sealed record PaymentInstrument
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static PaymentInstrument FromCard(CardDetails card) => new() { Card = card };
    public static PaymentInstrument FromVault(string vaultId) => new() { VaultId = vaultId };
}

/// <summary>Result of placing an authorization hold with PayPal.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? CardBrand,
    string? CardLastFour,
    string? CardExpiry);

/// <summary>Result of capturing an authorization, including PayPal's fee/net breakdown.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Result of renewing (re-authorizing) a stale hold.</summary>
public sealed record ReauthorizationResult(
    string AuthorizationId,
    string Status,
    string? ExpirationTime);

/// <summary>Result of refunding a captured payment.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting a card; carries only a safe descriptor plus the stored token id.</summary>
public sealed record VaultCardResult(
    string VaultId,
    string? CardBrand,
    string? CardLastFour,
    string? CardExpiry,
    string? Status);

/// <summary>A single transaction as PayPal's own reporting knows it.</summary>
public sealed record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? Date,
    string? EventCode);

/// <summary>A reconciliation line pairing a PayPal record against an eShop order (either side may be missing).</summary>
public sealed record ReconciliationLine(
    string Source,
    string? PayPalTransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? Date,
    int? OrderId);

/// <summary>The reconciliation report for a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
