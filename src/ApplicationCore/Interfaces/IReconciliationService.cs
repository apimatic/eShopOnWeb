using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction lines up against eShop's own records.</summary>
public enum ReconciliationMatch
{
    /// <summary>PayPal reports it and an eShop order references it.</summary>
    Matched = 0,

    /// <summary>PayPal reports it but no eShop order references it.</summary>
    InPayPalOnly = 1,

    /// <summary>An eShop order expects a payment PayPal does not report in this range.</summary>
    InEShopOnly = 2
}

/// <summary>One reconciled line.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? OrderReference,
    int? EShopOrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? Status,
    DateTimeOffset? TransactionDate);

/// <summary>
/// A reconciliation report over a date range: every PayPal transaction lined up against eShop
/// orders (both directions), plus whether the PayPal side was retrieved completely.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    bool PayPalDataComplete);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
