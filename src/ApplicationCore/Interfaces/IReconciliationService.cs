using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationLine(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? TransactionTime,
    int? MatchedOrderId,
    int? MatchedPaymentId,
    string MatchNote);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    IReadOnlyList<ReconciliationLine> PayPalTransactions,
    IReadOnlyList<ReconciliationUnmatchedLocal> LocalPaymentsNotInPayPalReport);

public record ReconciliationUnmatchedLocal(
    int PaymentId,
    int OrderId,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    string Note);

/// <summary>
/// Lines up PayPal's own record of transactions (transaction_search_v1) for a
/// date range against the payments eShop recorded, in both directions.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
