using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details, held only for the duration of a single gateway call. Nothing on this type is
/// ever persisted or logged — the application's database stores only what the processor's vault
/// hands back (a token id, brand, last four digits, expiry).
/// </summary>
public sealed record CardDetails
{
    public required string Number { get; init; }

    /// <summary>Expiry in <c>YYYY-MM</c> form.</summary>
    public required string Expiry { get; init; }

    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }

    /// <summary>
    /// Deliberately hides the card number from anything that stringifies this record — a log line,
    /// an exception message, a debugger dump.
    /// </summary>
    public override string ToString() => "CardDetails { redacted }";
}

public sealed record CardBillingAddress
{
    /// <summary>Two-letter ISO country code. The processor requires it on a billing address.</summary>
    public required string CountryCode { get; init; }

    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>What the shopper is paying with: a one-off card, or one of their saved cards.</summary>
public abstract record PaymentInstrument
{
    private PaymentInstrument() { }

    /// <summary>A card entered for this payment only. Never stored.</summary>
    public sealed record OneOffCard(CardDetails Card) : PaymentInstrument;

    /// <summary>
    /// One of the caller's own saved cards, named by its local id. This is the only form the API
    /// accepts — a caller never gets to hand us a processor vault id directly.
    /// </summary>
    public sealed record SavedCardReference(int PaymentMethodId) : PaymentInstrument;

    /// <summary>
    /// A card already in the processor's vault. Produced internally once ownership of the saved card
    /// has been confirmed; never built from caller input.
    /// </summary>
    public sealed record VaultToken(string VaultId) : PaymentInstrument;
}

public sealed record AuthorizationRequest
{
    public required int OrderId { get; init; }
    public required string InvoiceId { get; init; }
    public required decimal Amount { get; init; }
    public required string Description { get; init; }
    public required PaymentInstrument Instrument { get; init; }

    /// <summary>Idempotency key for creating the processor-side order.</summary>
    public required string CreateIdempotencyKey { get; init; }

    /// <summary>Idempotency key for placing the hold.</summary>
    public required string AuthorizeIdempotencyKey { get; init; }
}

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt);

public sealed record AuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? ExpiresAt);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public sealed record VaultedCard(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as the processor's own reporting knows it.</summary>
public sealed record GatewayTransaction(
    string TransactionId,
    string? Status,
    string? EventCode,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    string? CustomField);

public sealed record GatewayTransactionPage(
    IReadOnlyList<GatewayTransaction> Transactions,
    DateTimeOffset? LastRefreshedAt);
