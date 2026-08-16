using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction lines up (or fails to) against eShop's own records.</summary>
public enum ReconciliationMatchState
{
    /// <summary>PayPal knows this transaction and an eShop order references it.</summary>
    Matched,

    /// <summary>PayPal knows this transaction but no eShop order references it.</summary>
    InPayPalNotInEShop,

    /// <summary>eShop recorded this payment but PayPal's report has no matching transaction in range.</summary>
    InEShopNotInPayPal
}

/// <summary>One reconciled row. For eShop-only rows the PayPal fields are null.</summary>
public record ReconciliationLine(
    ReconciliationMatchState MatchState,
    string? PayPalTransactionId,
    string? PayPalStatus,
    string? EventCode,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    int? OrderId,
    string? EShopPaymentReference);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalNotInEShopCount,
    int InEShopNotInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds the reconciliation report for a date range: PayPal's own transaction record lined up against
/// eShop orders, so a payment one side knows about and the other doesn't is visible. Covers the whole
/// range, not just the first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
