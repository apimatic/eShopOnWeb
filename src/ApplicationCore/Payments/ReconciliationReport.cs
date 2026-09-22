using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a PayPal transaction and an eShop order line up in the reconciliation report.</summary>
public enum ReconciliationStatus
{
    /// <summary>Present on both sides and matched by invoice id.</summary>
    Matched = 0,
    /// <summary>PayPal knows about it; eShop has no matching order.</summary>
    InPayPalOnly = 1,
    /// <summary>eShop recorded a payment; PayPal has no transaction for it in this range.</summary>
    InEShopOnly = 2
}

public record ReconciliationLine
{
    public required ReconciliationStatus Status { get; init; }
    public string? InvoiceId { get; init; }
    public int? OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? EShopCapturedAmount { get; init; }
    public string? Currency { get; init; }
    public string? PayPalStatus { get; init; }
    public DateTimeOffset? PayPalInitiatedAt { get; init; }
}

public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int InPayPalOnlyCount { get; init; }
    public required int InEShopOnlyCount { get; init; }
    public required int PagesRead { get; init; }
    public required int TotalPages { get; init; }
    /// <summary>True if a hard page cap stopped the walk before covering the whole range.</summary>
    public required bool Truncated { get; init; }
}
