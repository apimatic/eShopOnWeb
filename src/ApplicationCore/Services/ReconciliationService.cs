using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's own record of transactions for the range — across every page.
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's settled records (captures and refunds) that carry a PayPal transaction id.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpec(), cancellationToken);
        var eShopRecords = BuildEShopRecords(orders);
        var eShopById = eShopRecords
            .GroupBy(r => r.PayPalId)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var seenPayPalIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tx in transactions)
        {
            seenPayPalIds.Add(tx.TransactionId);
            if (eShopById.TryGetValue(tx.TransactionId, out var match))
            {
                lines.Add(new ReconciliationLine(ReconciliationStatus.Matched, tx.TransactionId, tx.Status,
                    tx.Amount, tx.CurrencyCode ?? match.CurrencyCode, tx.Date, match.OrderId, match.Reference));
            }
            else
            {
                lines.Add(new ReconciliationLine(ReconciliationStatus.PayPalOnly, tx.TransactionId, tx.Status,
                    tx.Amount, tx.CurrencyCode, tx.Date, null, null));
            }
        }

        // eShop records PayPal did not return for this range (and whose activity falls in the range).
        foreach (var record in eShopRecords)
        {
            if (seenPayPalIds.Contains(record.PayPalId))
                continue;
            if (record.Date < from || record.Date > to)
                continue;

            lines.Add(new ReconciliationLine(ReconciliationStatus.EShopOnly, record.PayPalId, null,
                record.Amount, record.CurrencyCode, record.Date, record.OrderId, record.Reference));
        }

        return new ReconciliationReport(
            from,
            to,
            transactions.Count,
            lines.Count(l => l.Status == ReconciliationStatus.Matched),
            lines.Count(l => l.Status == ReconciliationStatus.PayPalOnly),
            lines.Count(l => l.Status == ReconciliationStatus.EShopOnly),
            lines);
    }

    private static List<EShopRecord> BuildEShopRecords(IReadOnlyList<Order> orders)
    {
        var records = new List<EShopRecord>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
                continue;

            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                records.Add(new EShopRecord(payment.CaptureId!, order.Id, $"order:{order.Id}:capture",
                    payment.CapturedGross ?? payment.Amount, payment.CurrencyCode, order.OrderDate));
            }

            foreach (var refund in payment.Refunds)
            {
                records.Add(new EShopRecord(refund.PayPalRefundId, order.Id, $"order:{order.Id}:refund",
                    refund.Amount, payment.CurrencyCode, refund.CreatedAt));
            }
        }
        return records;
    }

    private record EShopRecord(string PayPalId, int OrderId, string Reference, decimal Amount,
        string CurrencyCode, DateTimeOffset Date);
}
