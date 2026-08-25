using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    // PayPal's transaction search API caps a single request to a 31-day window.
    private static readonly TimeSpan MaxRangePerRequest = TimeSpan.FromDays(31);
    private const int PageSize = 500;
    private static readonly Regex InvoiceOrderIdRegex = new(@"^eShop-Order-(\d+)$", RegexOptions.Compiled);

    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    private record LocalTransaction(int OrderId, string EShopReference, string PayPalId, decimal Amount, string CurrencyCode);

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must not be before 'from'.");
        }

        var payPalTransactions = await FetchAllTransactionsAsync(from, to);
        var localTransactions = await LoadLocalTransactionsAsync(from, to);

        var payPalById = payPalTransactions.ToLookup(t => t.TransactionId);
        var localById = localTransactions.ToDictionary(t => t.PayPalId);

        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<string>();

        foreach (var ppTxn in payPalTransactions)
        {
            if (localById.TryGetValue(ppTxn.TransactionId, out var local))
            {
                matchedLocalIds.Add(local.PayPalId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatchStatus.Matched,
                    ppTxn.TransactionId, ppTxn.EventCode, ppTxn.Status, ppTxn.Amount,
                    local.OrderId, local.EShopReference, local.Amount, ppTxn.CurrencyCode));
            }
            else
            {
                var orderId = TryParseOrderId(ppTxn.InvoiceId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatchStatus.MissingInEShop,
                    ppTxn.TransactionId, ppTxn.EventCode, ppTxn.Status, ppTxn.Amount,
                    orderId, null, null, ppTxn.CurrencyCode));
            }
        }

        foreach (var local in localTransactions)
        {
            if (!matchedLocalIds.Contains(local.PayPalId) && !payPalById.Contains(local.PayPalId))
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatchStatus.MissingInPayPal,
                    local.PayPalId, null, null, null,
                    local.OrderId, local.EShopReference, local.Amount, local.CurrencyCode));
            }
        }

        return new ReconciliationReport(from, to, entries);
    }

    private async Task<List<PayPalTransactionRecord>> FetchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var results = new List<PayPalTransactionRecord>();

        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart + MaxRangePerRequest < to ? chunkStart + MaxRangePerRequest : to;

            var page = 1;
            var totalPages = 1;
            do
            {
                var pageResult = await _payPal.SearchTransactionsPageAsync(chunkStart, chunkEnd, page, PageSize);
                results.AddRange(pageResult.Transactions);
                totalPages = pageResult.TotalPages == 0 ? 1 : pageResult.TotalPages;
                page++;
            } while (page <= totalPages);

            chunkStart = chunkEnd;
        }

        return results;
    }

    private async Task<List<LocalTransaction>> LoadLocalTransactionsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var spec = new AllOrdersWithPaymentSpecification();
        var orders = await _orderRepository.ListAsync(spec);

        var local = new List<LocalTransaction>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (payment.CaptureId is not null && payment.CaptureTime is not null && IsInRange(payment.CaptureTime.Value, from, to))
            {
                local.Add(new LocalTransaction(order.Id, "capture", payment.CaptureId, payment.CapturedAmount ?? 0m, payment.CurrencyCode));
            }

            foreach (var refund in payment.Refunds)
            {
                if (IsInRange(refund.CreateTime, from, to))
                {
                    local.Add(new LocalTransaction(order.Id, "refund", refund.PayPalRefundId, refund.Amount, refund.CurrencyCode));
                }
            }
        }

        return local;
    }

    private static bool IsInRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) => value >= from && value <= to;

    private static int? TryParseOrderId(string? invoiceId)
    {
        if (invoiceId is null)
        {
            return null;
        }
        var match = InvoiceOrderIdRegex.Match(invoiceId);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
