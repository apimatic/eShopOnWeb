using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own record of transactions for a date range up against eShop orders, so a payment
/// PayPal knows about that eShop doesn't — or the reverse — is visible. The linkage key is the
/// eShop order id, which is stamped on every PayPal order/capture as <c>invoice_id</c>; the capture
/// id is used as a secondary key.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalReconciliation _reconciliation;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalReconciliation reconciliation, IReadRepository<Order> orderRepository)
    {
        _reconciliation = reconciliation;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _reconciliation.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new CapturedOrdersInRangeSpec(from, to), cancellationToken);

        var ordersByInvoice = orders
            .Where(o => o.PayPalInvoiceReference is not null)
            .GroupBy(o => o.PayPalInvoiceReference!)
            .ToDictionary(g => g.Key, g => g.First());
        var ordersByCapture = orders
            .Where(o => o.PayPalCaptureId is not null)
            .GroupBy(o => o.PayPalCaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            EShopCapturedOrderCount = orders.Count
        };

        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            Order? match = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId) && ordersByInvoice.TryGetValue(txn.InvoiceId, out var byInvoice))
            {
                match = byInvoice;
            }
            else if (!string.IsNullOrEmpty(txn.TransactionId) && ordersByCapture.TryGetValue(txn.TransactionId, out var byCapture))
            {
                match = byCapture;
            }

            if (match is not null)
            {
                matchedOrderIds.Add(match.Id);
                var amountsAgree = match.CapturedAmount.HasValue && txn.Amount.HasValue
                    && decimal.Compare(Math.Abs(match.CapturedAmount.Value), Math.Abs(txn.Amount.Value)) == 0;
                report.Matched.Add(new ReconciliationMatch(
                    match.Id,
                    match.PayPalCaptureId,
                    txn.TransactionId,
                    match.CapturedAmount,
                    txn.Amount,
                    txn.Status,
                    amountsAgree));
            }
            else
            {
                report.InPayPalOnly.Add(new ReconciliationPayPalOnly(
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.EventCode,
                    txn.Status,
                    txn.Amount,
                    txn.Currency,
                    txn.InitiationDate));
            }
        }

        foreach (var order in orders.Where(o => !matchedOrderIds.Contains(o.Id)))
        {
            report.InEShopOnly.Add(new ReconciliationEShopOnly(
                order.Id,
                order.PayPalCaptureId,
                order.CapturedAmount,
                order.Currency,
                order.OrderDate));
        }

        return report;
    }
}
