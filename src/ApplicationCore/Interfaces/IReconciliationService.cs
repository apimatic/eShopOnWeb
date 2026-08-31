using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    /// <summary>
    /// Lines up PayPal's own record of transactions for the range against eShop orders.
    /// Covers the whole range (all report pages).
    /// </summary>
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalPayPalTransactions,
    int MatchedTransactions,
    IReadOnlyList<ReconciliationTransaction> Transactions,
    IReadOnlyList<ReconciliationLocalPayment> EshopPaymentsNotInPayPalReport);

public sealed record ReconciliationTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    string? Amount,
    string? Currency,
    string? FeeAmount,
    string? InvoiceId,
    DateTimeOffset? InitiationDate,
    bool MatchedToEshopOrder,
    int? EshopOrderId,
    string? MatchType);

public sealed record ReconciliationLocalPayment(
    int OrderId,
    string BuyerId,
    string Status,
    string? AuthorizationId,
    string? CaptureId,
    decimal? CapturedAmount,
    string Currency);
