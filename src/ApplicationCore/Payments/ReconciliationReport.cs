using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Whether a reconciliation entry was seen on both sides, only at PayPal, or only in eShop.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal reports a transaction that lines up with an eShop payment.</summary>
    Matched,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    PayPalOnly,

    /// <summary>eShop recorded a payment PayPal's report does not (yet) list.</summary>
    EShopOnly
}

/// <summary>One line of the reconciliation report — a PayPal transaction, an eShop payment, or both lined up.</summary>
public class ReconciliationEntry
{
    public ReconciliationMatch Match { get; init; }
    public int? OrderId { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalTransactionStatus { get; init; }
    public decimal? EShopAmount { get; init; }
    public decimal? PayPalAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? Date { get; init; }
}

/// <summary>
/// Lines PayPal's own transaction record up against eShop orders for a date range, so a payment PayPal
/// knows about but eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int PayPalTransactionCount { get; init; }
    public int EShopPaymentCount { get; init; }
    public int MatchedCount { get; init; }
    public int PayPalOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();
}
