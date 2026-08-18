using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    private record Settlement(string TransactionId, int OrderId, string Kind, decimal Amount, string Currency,
        DateTimeOffset When);

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        // eShop's own settlement records (captures + refunds) drawn from the orders' payment state.
        var allSettlements = new List<Settlement>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;

            if (!string.IsNullOrEmpty(payment.CaptureId) && payment.CapturedAmount is decimal captured)
            {
                allSettlements.Add(new Settlement(payment.CaptureId!, order.Id, "capture", captured,
                    payment.CurrencyCode, payment.CapturedAt ?? order.OrderDate));
            }

            foreach (var refund in payment.Refunds)
            {
                allSettlements.Add(new Settlement(refund.RefundId, order.Id, "refund", refund.Amount,
                    refund.CurrencyCode, refund.CreatedAt));
            }
        }

        var settlementById = allSettlements
            .GroupBy(s => s.TransactionId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // eShop settlements that fall inside the requested range are the ones PayPal's report should show.
        var inRangeSettlements = allSettlements
            .Where(s => s.When >= from && s.When <= to)
            .ToList();

        var payPalById = payPalTransactions
            .Where(t => !string.IsNullOrEmpty(t.TransactionId))
            .GroupBy(t => t.TransactionId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationEntry>();
        var inPayPalNotInEShop = new List<ReconciliationEntry>();

        foreach (var txn in payPalTransactions)
        {
            if (txn.TransactionId is not null && settlementById.TryGetValue(txn.TransactionId, out var settlement))
            {
                var amountMatches = txn.Amount is decimal a && Round(Math.Abs(a)) == Round(settlement.Amount);
                matched.Add(new ReconciliationEntry(txn.TransactionId, settlement.OrderId, settlement.Kind,
                    settlement.Amount, txn.Amount, settlement.Currency ?? txn.Currency, txn.Status, txn.Date, amountMatches));
            }
            else
            {
                inPayPalNotInEShop.Add(new ReconciliationEntry(txn.TransactionId, null, "unknown",
                    null, txn.Amount, txn.Currency, txn.Status, txn.Date, false));
            }
        }

        var inEShopNotInPayPal = inRangeSettlements
            .Where(s => !payPalById.ContainsKey(s.TransactionId))
            .Select(s => new ReconciliationEntry(s.TransactionId, s.OrderId, s.Kind, s.Amount, null, s.Currency,
                null, s.When, false))
            .ToList();

        return new ReconciliationReport(from, to, payPalTransactions.Count, inRangeSettlements.Count,
            matched, inPayPalNotInEShop, inEShopNotInPayPal);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
