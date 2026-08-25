using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPalGateway;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPalGateway)
    {
        _orderRepository = orderRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var searchResult = await _payPalGateway.SearchTransactionsAsync(from, to, ct);
        var remainingPayPalTransactions = searchResult.Transactions.ToDictionary(t => t.TransactionId, t => t);

        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivityInRangeSpecification(from, to), ct);

        var entries = new List<ReconciliationEntry>();

        foreach (var order in orders)
        {
            var payment = order.Payment!;

            if (payment.CapturedOn.HasValue && payment.CapturedOn.Value >= from && payment.CapturedOn.Value <= to)
            {
                entries.Add(BuildEntry(order.Id, payment.CaptureId, payment.CapturedAmount, payment.CaptureStatus,
                    remainingPayPalTransactions));
            }

            foreach (var refund in payment.Refunds.Where(r => r.CreatedOn >= from && r.CreatedOn <= to))
            {
                entries.Add(BuildEntry(order.Id, refund.PayPalRefundId, refund.Amount, refund.Status,
                    remainingPayPalTransactions));
            }
        }

        // Whatever remains is a transaction PayPal reports that this pass could not match to any
        // eShop order/refund in range — the other half of the reconciliation gap this report exists for.
        foreach (var unmatched in remainingPayPalTransactions.Values)
        {
            entries.Add(new ReconciliationEntry(null, unmatched.TransactionId, null, unmatched.Amount, null,
                unmatched.Status, ReconciliationMatchStatus.PayPalOnly));
        }

        return new ReconciliationReport(from, to, entries, searchResult.Warnings);
    }

    private static ReconciliationEntry BuildEntry(int orderId, string? transactionId, decimal? eShopAmount,
        string? eShopStatus, Dictionary<string, TransactionRecord> remainingPayPalTransactions)
    {
        if (transactionId is not null && remainingPayPalTransactions.TryGetValue(transactionId, out var match))
        {
            remainingPayPalTransactions.Remove(transactionId);
            return new ReconciliationEntry(orderId, transactionId, eShopAmount, match.Amount, eShopStatus, match.Status,
                ReconciliationMatchStatus.Matched);
        }

        return new ReconciliationEntry(orderId, transactionId, eShopAmount, null, eShopStatus, null,
            ReconciliationMatchStatus.EShopOnly);
    }
}
