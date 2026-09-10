using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One reconciled item: a PayPal transaction and/or the eShop order it lines up with.</summary>
public record ReconciliationEntry(
    string Status,                 // MATCHED, MISSING_IN_ESHOP, MISSING_IN_PAYPAL
    string? PayPalTransactionId,
    string? EventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? CurrencyCode,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? PayPalOrderId,
    decimal? EShopAmount,
    string? EShopPaymentStatus);

/// <summary>
/// A reconciliation report over a date range: every PayPal transaction lined up against eShop orders,
/// so a payment one side knows about and the other doesn't is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IReconciliationService
{
    /// <summary>Builds the report for the whole [from, to] range (paging through all PayPal results).</summary>
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
