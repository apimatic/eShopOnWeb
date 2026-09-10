using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One PayPal transaction lined up against an eShop order (or the lack of one).</summary>
public record ReconciliationEntry(
    string? TransactionId,
    string TransactionStatus,
    decimal? PayPalAmount,
    string? Currency,
    int? OrderId,
    string? OrderStatus,
    decimal? OrderCapturedAmount,
    string Classification); // "Matched" | "InPayPalNotInEShop" | "InEShopNotInPayPal"

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int InPayPalNotInEShopCount,
    int InEShopNotInPayPalCount,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
