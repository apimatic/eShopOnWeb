using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop order line up (or fail to) during reconciliation.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal transaction matched to an eShop order.</summary>
    Matched = 0,

    /// <summary>PayPal knows about the transaction but no eShop order matches it.</summary>
    InPayPalOnly = 1,

    /// <summary>eShop recorded a captured payment PayPal's report does not (yet) show.</summary>
    InEShopOnly = 2
}

/// <summary>One reconciled row: a PayPal transaction and/or an eShop order, with the match verdict.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    string? CurrencyCode,
    int? OrderId,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

/// <summary>The full reconciliation report for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Builds a reconciliation report by pulling PayPal's own record of transactions for a date range
/// (across the whole range, not just the first page) and lining them up against eShop orders.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
