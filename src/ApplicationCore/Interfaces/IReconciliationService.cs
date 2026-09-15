using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report lining PayPal's own record of transactions for a date range up
/// against eShop orders, so a payment PayPal knows about that eShop doesn't — or the reverse — is
/// visible. Covers the whole range, not just the first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<ReconciliationRow> InPayPalOnly,
    IReadOnlyList<ReconciliationRow> InEShopOnly);

/// <summary>One reconciled line. Either side may be absent, which is exactly what the report surfaces.</summary>
public record ReconciliationRow(
    string? InvoiceId,
    // eShop side
    int? OrderId,
    string? OrderStatus,
    decimal? EShopCapturedAmount,
    string? PayPalOrderId,
    string? CaptureId,
    // PayPal side
    string? PayPalTransactionId,
    string? PayPalTransactionStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? PayPalDate,
    string Discrepancy);
