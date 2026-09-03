using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction lines up (or fails to) against this app's records.</summary>
public enum ReconciliationStatus
{
    /// <summary>PayPal and eShop both know this transaction.</summary>
    Matched,

    /// <summary>PayPal reported it, but no eShop order references it.</summary>
    PayPalOnly,

    /// <summary>eShop recorded a PayPal id, but PayPal's reporting did not return it for the range.</summary>
    EShopOnly
}

/// <summary>One reconciled line: a PayPal transaction and/or the eShop order it maps to.</summary>
public record ReconciliationLine(
    ReconciliationStatus Status,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopReference);

/// <summary>A reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds a reconciliation report that lists PayPal's own record of transactions for a date range
/// and lines them up against eShop orders, so a payment one side knows about and the other does not
/// becomes visible. Covers the whole range, not just its first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
