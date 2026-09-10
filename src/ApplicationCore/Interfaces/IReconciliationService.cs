using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop payment line up during reconciliation.</summary>
public enum ReconciliationMatch
{
    /// <summary>Present on both sides.</summary>
    Matched = 0,

    /// <summary>PayPal reports it, eShop has no matching payment.</summary>
    PayPalOnly = 1,

    /// <summary>eShop expects it, PayPal's report does not show it.</summary>
    EShopOnly = 2
}

/// <summary>One reconciled line pairing PayPal's record with eShop's, where each exists.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? PayPalTransactionId,
    string? InvoiceId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate,
    int? EShopOrderId,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Builds a report lining PayPal's own transaction record against eShop orders over a date range,
/// so a payment one side knows about and the other does not is visible. Covers the whole range.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
