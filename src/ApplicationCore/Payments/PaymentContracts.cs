using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off card payment or a card to vault. Never persisted or logged by the app.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry, // ISO-8601 YYYY-MM
    string SecurityCode,
    string? CardholderName,
    string? BillingStreet,
    string? BillingCity,
    string? BillingState,
    string? BillingCountryCode,
    string? BillingPostalCode);

/// <summary>How an order is to be paid: either raw card details, or a saved card's PayPal vault token id. Exactly one is set.</summary>
public sealed record PaymentInstrument(CardDetails? Card, string? VaultId)
{
    public static PaymentInstrument FromCard(CardDetails card) => new(card, null);
    public static PaymentInstrument FromVault(string vaultId) => new(null, vaultId);
}

/// <summary>Result of authorizing (holding) an order total with PayPal.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorized payment at fulfilment, with what PayPal reported.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string? Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Current PayPal state of an authorization — used to decide renewal and to settle unknown outcomes.</summary>
public sealed record AuthorizationStatusInfo(string? Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of renewing (reauthorizing) a stale authorization.</summary>
public sealed record RenewalResult(string AuthorizationId, string? Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of a refund against a captured payment.</summary>
public sealed record RefundResult(string RefundId, string? Status, decimal Amount);

/// <summary>Result of vaulting a card: the token id plus a safe display of the card.</summary>
public sealed record VaultCardResult(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One PayPal transaction as reported by transaction search.</summary>
public sealed record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? Status,
    DateTimeOffset? InitiationDate);

/// <summary>All PayPal transactions for a date range, with whether the walk covered the whole range.</summary>
public sealed record TransactionSearchResult(
    IReadOnlyList<PayPalTransaction> Transactions,
    int PagesFetched,
    int TotalPages,
    bool Complete);
