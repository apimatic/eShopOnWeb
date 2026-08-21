using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines PayPal's own record of transactions up against eShop's payments over a date range, so a payment
/// PayPal knows about that eShop doesn't — or the reverse — is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>One PayPal transaction aligned (or not) to an eShop order.</summary>
public record ReconciliationLine(
    string TransactionId,
    decimal? Amount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    int? MatchedOrderId);

/// <summary>An eShop payment PayPal's reporting did not return for the range.</summary>
public record UnmatchedEShopPayment(
    int OrderId,
    string? CaptureId,
    decimal? CapturedAmount,
    string CurrencyCode,
    string PaymentStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationLine> PayPalTransactions,
    IReadOnlyList<ReconciliationLine> PayPalTransactionsWithoutEShopOrder,
    IReadOnlyList<UnmatchedEShopPayment> EShopPaymentsWithoutPayPalRecord);
