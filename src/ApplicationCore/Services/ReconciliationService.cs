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

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's record across the whole range (the gateway pages through everything).
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payPalById = payPalTransactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's record: captures and refunds that fall within the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), ct);
        var eShopTransactions = BuildEShopTransactions(orders, from, to);

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<EShopTransaction>();
        var matchedPayPalIds = new HashSet<string>();

        foreach (var tx in eShopTransactions)
        {
            if (payPalById.TryGetValue(tx.TransactionId, out var pp))
            {
                matchedPayPalIds.Add(tx.TransactionId);
                matched.Add(new ReconciliationMatch(
                    tx.TransactionId, tx.Kind, tx.OrderId, tx.Amount, pp.Amount, pp.Status,
                    AmountMatches: Math.Abs(tx.Amount - pp.Amount) < 0.01m));
            }
            else
            {
                eShopOnly.Add(tx);
            }
        }

        var payPalOnly = payPalTransactions
            .Where(t => !matchedPayPalIds.Contains(t.TransactionId))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
    }

    private static List<EShopTransaction> BuildEShopTransactions(IEnumerable<Order> orders, DateTimeOffset from, DateTimeOffset to)
    {
        var list = new List<EShopTransaction>();
        foreach (var order in orders)
        {
            var p = order.Payment;
            if (p is null) continue;

            if (p.CaptureId is not null && p.CapturedGross is not null &&
                p.CapturedAt is not null && p.CapturedAt >= from && p.CapturedAt <= to)
            {
                list.Add(new EShopTransaction(p.CaptureId, "capture", p.CapturedGross.Value, order.Id, p.CaptureStatus, p.CapturedAt.Value));
            }

            foreach (var refund in p.Refunds)
            {
                if (refund.PayPalRefundId is not null && refund.CreatedAt >= from && refund.CreatedAt <= to)
                {
                    list.Add(new EShopTransaction(refund.PayPalRefundId, "refund", refund.Amount, order.Id, refund.Status, refund.CreatedAt));
                }
            }
        }
        return list;
    }
}
