using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ReconciliationStatus
{
    /// <summary>Present in both PayPal and eShop.</summary>
    Matched,
    /// <summary>PayPal has the transaction but eShop has no matching order.</summary>
    MissingInEShop,
    /// <summary>eShop recorded the capture but no matching PayPal transaction was found in range.</summary>
    MissingInPayPal
}

public record ReconciliationLine(
    ReconciliationStatus Status,
    string? ReconciliationReference,
    int? OrderId,
    string? PayPalTransactionId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? CurrencyCode,
    DateTimeOffset? TransactionDate);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionsInRange,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds a reconciliation report lining up PayPal's own transaction records against eShop orders
/// for a date range, surfacing discrepancies in both directions.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
