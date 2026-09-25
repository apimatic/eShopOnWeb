using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. These are passed straight
/// to PayPal and NEVER persisted in this application's database nor written to logs.
/// </summary>
public record CardDetails
{
    public required string Number { get; init; }
    /// <summary>ISO-8601 YYYY-MM.</summary>
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    // Optional billing address (helps AVS on direct card processing).
    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    /// <summary>2-char ISO-3166-1 country code. Required by PayPal when a billing address is sent.</summary>
    public string? BillingCountryCode { get; init; }
}

/// <summary>Instruction to authorize (hold) an order total via a card or a saved (vaulted) card.</summary>
public record AuthorizeCommand
{
    public required int OrderId { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required string ReconciliationReference { get; init; }
    public string? Description { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>One-off card details (mutually exclusive with <see cref="VaultId"/>).</summary>
    public CardDetails? Card { get; init; }

    /// <summary>Saved-card vault token id (mutually exclusive with <see cref="Card"/>).</summary>
    public string? VaultId { get; init; }
}

public record AuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? OrderStatus { get; init; }
}

public record AuthorizationInfo
{
    public required string AuthorizationId { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record CaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
}

public record RefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
}

/// <summary>Instruction to vault (save) a card for a shopper.</summary>
public record VaultCardCommand
{
    public required CardDetails Card { get; init; }
    /// <summary>Existing PayPal customer id to attach the card to, if the shopper already has one.</summary>
    public string? PayPalCustomerId { get; init; }
    /// <summary>Stable merchant-side customer id (derived from the shopper), used when no PayPal id exists yet.</summary>
    public required string MerchantCustomerId { get; init; }
    public required string IdempotencyKey { get; init; }
}

public record VaultCardResult
{
    public required string VaultId { get; init; }
    public required string PayPalCustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>One PayPal transaction as reported by TransactionSearch.</summary>
public record ReconciliationTransaction
{
    public string? TransactionId { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? CustomField { get; init; }
    public string? InvoiceId { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
}

/// <summary>PayPal's own record of transactions for a date range (the whole range, all pages).</summary>
public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationTransaction> Transactions { get; init; }
    public required int PagesFetched { get; init; }
    public int? TotalItems { get; init; }
    /// <summary>True when the whole range was walked; false if a safety cap truncated it.</summary>
    public required bool Complete { get; init; }
    public int? TruncatedAtPage { get; init; }
}
