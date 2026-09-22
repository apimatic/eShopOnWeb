using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Instruction to authorize an order total: hold the money, do not take it.</summary>
public record AuthorizeCommand
{
    public required decimal Amount { get; init; }
    public required string InvoiceId { get; init; }
    public required string CustomId { get; init; }
    public string? Description { get; init; }
    public required string CreateRequestId { get; init; }
    public required string AuthorizeRequestId { get; init; }

    /// <summary>Raw card for a one-off payment, XOR <see cref="VaultTokenId"/>.</summary>
    public CardDetails? Card { get; init; }
    /// <summary>A saved card's PayPal vault token id, XOR <see cref="Card"/>.</summary>
    public string? VaultTokenId { get; init; }
}

public record AuthorizeResult
{
    public required bool Success { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public string? FailureReason { get; init; }
}

public record CaptureResult
{
    public required bool Success { get; init; }
    public string? CaptureId { get; init; }
    public string? Status { get; init; }
    public decimal CapturedAmount { get; init; }
    public decimal? PaypalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string? FailureReason { get; init; }
}

public record ReauthorizeResult
{
    public required bool Success { get; init; }
    public string? AuthorizationId { get; init; }
    public string? Status { get; init; }
    public string? FailureReason { get; init; }
}

public record VoidResult
{
    public required bool Success { get; init; }
    public string? Status { get; init; }
    public string? FailureReason { get; init; }
}

public record RefundResult
{
    public required bool Success { get; init; }
    public string? RefundId { get; init; }
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public string? FailureReason { get; init; }
}

public record VaultCardResult
{
    public required string TokenId { get; init; }
    public string? CustomerId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>A single PayPal transaction as PayPal reports it, for reconciliation.</summary>
public record GatewayTransaction
{
    public string? TransactionId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? Fee { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? InitiatedAt { get; init; }
}

/// <summary>Result of a transaction search covering a whole date range across pages.</summary>
public record TransactionSearchResult
{
    public required IReadOnlyList<GatewayTransaction> Transactions { get; init; }
    public required int PagesRead { get; init; }
    public required int TotalPages { get; init; }
    /// <summary>True if a hard page cap stopped the walk before the last page.</summary>
    public required bool Truncated { get; init; }
}
