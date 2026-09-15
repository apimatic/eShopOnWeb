using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report for a date range: PayPal's own record of transactions lined up
/// against eShop orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is
/// visible. Covers the whole range, paging and windowing internally.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaidOrderCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> InPayPalNotEShop,
    IReadOnlyList<ReconciliationLine> InEShopNotPayPal);

/// <summary>One reconciled line pairing (where possible) a PayPal transaction with an eShop order.</summary>
public record ReconciliationLine(
    string? PaymentReference,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    DateTimeOffset? PayPalDate,
    int? OrderId,
    string? OrderStatus,
    decimal? OrderAmount,
    string Note);
