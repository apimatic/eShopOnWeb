using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop order line up during reconciliation.</summary>
public enum ReconciliationStatus
{
    /// <summary>Present on both sides and matched by reference.</summary>
    Matched = 0,

    /// <summary>PayPal recorded a transaction that no eShop order accounts for.</summary>
    MissingInEShop = 1,

    /// <summary>eShop captured a payment that PayPal's report does not (yet) show.</summary>
    MissingInPayPal = 2,
}

/// <summary>One reconciled line: a PayPal transaction, an eShop order, or both.</summary>
public record ReconciliationLine(
    ReconciliationStatus Status,
    string Reference,
    int? OrderId,
    decimal? EShopAmount,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    string Currency);

/// <summary>The full reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> Lines)
{
    public int MatchedCount { get; init; }
    public int MissingInEShopCount { get; init; }
    public int MissingInPayPalCount { get; init; }
}

/// <summary>
/// Operator report that lists PayPal's own record of transactions for a date range and lines it up
/// against eShop orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
