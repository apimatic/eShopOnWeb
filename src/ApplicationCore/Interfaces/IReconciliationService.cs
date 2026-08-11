using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop payment record line up during reconciliation.</summary>
public enum ReconciliationOutcome
{
    /// <summary>Present on both sides — PayPal reports it and eShop recognises the id.</summary>
    Matched = 0,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    InPayPalOnly = 1,

    /// <summary>eShop expects a transaction PayPal's report does not (yet) show.</summary>
    InEShopOnly = 2
}

/// <summary>One reconciled line: a PayPal transaction and/or the eShop payment it belongs to.</summary>
public record ReconciliationEntry(
    ReconciliationOutcome Outcome,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? EShopRecordType,
    decimal? EShopAmount);

/// <summary>The full reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Builds a reconciliation report over a date range, lining up PayPal's own record of transactions
/// against eShop's payment records so a discrepancy either way is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
