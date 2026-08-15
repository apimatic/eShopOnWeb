using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>How a PayPal transaction and an eShop record line up.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal knows about it and so does eShop.</summary>
    Matched,

    /// <summary>PayPal knows about it but eShop has no matching payment/refund.</summary>
    PayPalOnly,

    /// <summary>eShop recorded it but PayPal's report does not list it (may be reporting lag).</summary>
    EShopOnly
}

/// <summary>A single reconciled row.</summary>
public record ReconciliationLine(
    string PayPalTransactionId,
    ReconciliationMatch Match,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    int? OrderId,
    string? Kind);

/// <summary>The reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);
