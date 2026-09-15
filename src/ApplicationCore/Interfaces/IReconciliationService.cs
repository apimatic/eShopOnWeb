using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Which side(s) of the reconciliation a line was found on.</summary>
public enum ReconciliationSide
{
    /// <summary>PayPal reports it and eShop has a matching payment.</summary>
    Matched = 0,

    /// <summary>PayPal reports a transaction eShop has no record of.</summary>
    PayPalOnly = 1,

    /// <summary>eShop has a payment PayPal's report does not (yet) show.</summary>
    EShopOnly = 2
}

public record ReconciliationLine(
    ReconciliationSide Side,
    string? TransactionId,
    string? TransactionStatus,
    string? EventCode,
    DateTimeOffset? TransactionDate,
    decimal? PayPalAmount,
    string? Currency,
    int? OrderId,
    PaymentStatus? LocalStatus,
    decimal? LocalAmount,
    string? PayPalOrderId);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int LocalPaymentCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Builds a reconciliation report that lists PayPal's own record of transactions for a date range
/// and lines them up against eShop orders, surfacing anything present on only one side.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
