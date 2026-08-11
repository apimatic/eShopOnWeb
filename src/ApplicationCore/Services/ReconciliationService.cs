using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Produces the reconciliation report by pulling PayPal's transaction records for a date range (covering the
/// whole range: ≤ 31-day windows, every page) and lining them up against eShop's own order payments by invoice id.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    // PayPal's transaction-search window is capped at 31 days and page size at 500.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);
    private const int PageSize = 500;
    private const int MaxPagesPerWindow = 10_000; // safety valve against a runaway paging loop

    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalGateway payPal,
        IReadRepository<OrderPayment> paymentRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("The reconciliation 'to' must be on or after 'from'.");
        }

        var payPalTransactions = await FetchAllTransactionsAsync(from, to, cancellationToken);

        var eShopPayments = await _paymentRepository.ListAsync(new PaidOrderPaymentsSpec(), cancellationToken);
        var paymentsByInvoice = eShopPayments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lines = new List<ReconciliationLine>();
        var matchedInvoices = new HashSet<string>(StringComparer.Ordinal);

        // Every PayPal transaction, matched to an eShop order by invoice id where possible.
        foreach (var txn in payPalTransactions)
        {
            OrderPayment? payment = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId))
            {
                paymentsByInvoice.TryGetValue(txn.InvoiceId!, out payment);
            }

            if (payment is not null)
            {
                matchedInvoices.Add(payment.InvoiceId);
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.Matched,
                    OrderId = payment.OrderId,
                    InvoiceId = txn.InvoiceId,
                    PayPalTransactionId = txn.TransactionId,
                    EventCode = txn.EventCode,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount?.Value,
                    EShopAmount = payment.Amount,
                    EShopPaymentStatus = payment.Status.ToString(),
                    Date = txn.InitiationDate ?? txn.UpdatedDate
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.MissingInEShop,
                    InvoiceId = txn.InvoiceId,
                    PayPalTransactionId = txn.TransactionId,
                    EventCode = txn.EventCode,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount?.Value,
                    Date = txn.InitiationDate ?? txn.UpdatedDate,
                    Note = "PayPal has this transaction but eShop has no matching order."
                });
            }
        }

        // eShop payments PayPal's report does not (yet) show — often just PayPal's reporting lag.
        foreach (var payment in eShopPayments)
        {
            if (string.IsNullOrEmpty(payment.InvoiceId) || matchedInvoices.Contains(payment.InvoiceId))
            {
                continue;
            }
            lines.Add(new ReconciliationLine
            {
                Match = ReconciliationMatch.MissingInPayPal,
                OrderId = payment.OrderId,
                InvoiceId = payment.InvoiceId,
                EShopAmount = payment.Amount,
                EShopPaymentStatus = payment.Status.ToString(),
                Date = payment.CreatedAt,
                Note = "eShop recorded this payment but PayPal's report does not list it yet (transaction reporting can lag by up to 3 hours)."
            });
        }

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = payPalTransactions.Count,
            EShopPaymentCount = eShopPayments.Count,
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            MissingInEShopCount = lines.Count(l => l.Match == ReconciliationMatch.MissingInEShop),
            MissingInPayPalCount = lines.Count(l => l.Match == ReconciliationMatch.MissingInPayPal),
            Lines = lines
        };

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {report.PayPalTransactionCount} PayPal txns, {report.EShopPaymentCount} eShop payments, " +
            $"{report.MatchedCount} matched, {report.MissingInEShopCount} PayPal-only, {report.MissingInPayPalCount} eShop-only.");

        return report;
    }

    private async Task<List<PayPalTransaction>> FetchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<PayPalTransaction>();
        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to))
        {
            var page = 1;
            while (true)
            {
                var result = await _payPal.SearchTransactionsAsync(
                    new TransactionSearchQuery(windowStart, windowEnd, page, PageSize), cancellationToken);
                all.AddRange(result.Transactions);

                if (result.TotalPages <= page || page >= MaxPagesPerWindow)
                {
                    break;
                }
                page++;
            }
        }
        return all;
    }

    /// <summary>Splits an arbitrary range into consecutive windows of at most 31 days each.</summary>
    private static IEnumerable<(DateTimeOffset start, DateTimeOffset end)> SplitIntoWindows(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to)
        {
            yield return (from, to);
            yield break;
        }
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor + MaxWindow;
            if (end > to)
            {
                end = to;
            }
            yield return (cursor, end);
            cursor = end;
        }
    }
}
