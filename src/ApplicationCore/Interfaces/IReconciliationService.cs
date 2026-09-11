using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the provider's own record of transactions for a date range against eShop's orders,
/// so a payment the provider knows about but eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> ProviderOnly,
    IReadOnlyList<ReconciliationLine> EShopOnly);

/// <summary>A single reconciled row. Fields are populated according to which side(s) it appears on.</summary>
public record ReconciliationLine(
    string? OrderReference,
    int? OrderId,
    // provider side
    string? ProviderTransactionId,
    string? ProviderEventCode,
    string? ProviderStatus,
    decimal? ProviderAmount,
    string? ProviderCurrency,
    DateTimeOffset? ProviderDate,
    // eShop side
    string? EShopStatus,
    decimal? EShopCapturedAmount,
    string? EShopCaptureId);
