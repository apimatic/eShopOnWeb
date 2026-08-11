using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a payment lines up between PayPal's records and eShop's own.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal and eShop both know about it.</summary>
    Matched,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    PayPalOnly,

    /// <summary>eShop has a payment PayPal did not report in this range (e.g. reporting lag).</summary>
    EShopOnly
}

public record ReconciliationLine(
    ReconciliationMatch Match,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? EShopStatus,
    decimal? EShopAmount,
    string CurrencyCode);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines)
{
    public int MatchedCount { get; init; }
    public int PayPalOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
}

public interface IReconciliationService
{
    /// <summary>
    /// Builds a reconciliation report over the whole date range (walking every page of PayPal's
    /// transaction report) and lines PayPal's transactions up against eShop orders/payments.
    /// </summary>
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
