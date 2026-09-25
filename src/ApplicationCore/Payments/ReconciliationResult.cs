using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a reconciliation line lines up between PayPal and eShop.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal and eShop both know about this payment.</summary>
    Matched = 0,
    /// <summary>PayPal reported a transaction eShop has no record of.</summary>
    PayPalOnly = 1,
    /// <summary>eShop recorded a payment PayPal's report does not show (may be reporting lag).</summary>
    EShopOnly = 2
}

public record ReconciliationLine
{
    public required ReconciliationMatch Match { get; init; }
    public int? OrderId { get; init; }
    public string? Reference { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public string? EventCode { get; init; }
    public decimal? PayPalAmount { get; init; }
    public string? CurrencyCode { get; init; }
    public decimal? EShopAmount { get; init; }
    public string? EShopPaymentStatus { get; init; }
    public DateTimeOffset? PayPalDate { get; init; }
}

/// <summary>
/// PayPal's own transaction record for a date range lined up against eShop orders, so a payment
/// PayPal knows about that eShop doesn't (or the reverse) is visible. Covers the whole range.
/// </summary>
public record ReconciliationResult
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int PayPalOnlyCount { get; init; }
    public required int EShopOnlyCount { get; init; }
    public required int PagesFetched { get; init; }
    /// <summary>True when the whole range was walked; false if a safety cap truncated it.</summary>
    public required bool Complete { get; init; }
    public int? TruncatedAtPage { get; init; }
}
