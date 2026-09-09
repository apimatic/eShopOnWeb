using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>How a PayPal transaction and an eShop order line up during reconciliation.</summary>
public enum ReconciliationState
{
    /// <summary>Present in both PayPal's ledger and eShop.</summary>
    Matched = 0,

    /// <summary>PayPal knows about it, eShop has no matching order/payment.</summary>
    MissingInEShop = 1,

    /// <summary>eShop recorded a payment PayPal's ledger does not show for this range.</summary>
    MissingInPayPal = 2
}

/// <summary>A single reconciled line pairing (where present) a PayPal transaction and an eShop order.</summary>
public record ReconciliationEntry(
    ReconciliationState State,
    string? PayPalTransactionId,
    string? InvoiceId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    decimal? OrderAmount,
    string? PaymentStatus);

/// <summary>The full reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Builds a reconciliation report lining PayPal's own transaction record for a date range up
/// against eShop orders, covering the whole range (all pages), so a payment one side knows about
/// and the other does not is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);
}
