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
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPalGateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway payPalGateway)
    {
        _orderRepository = orderRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _payPalGateway.SearchTransactionsAsync(from, to, ct);
        var transactionsById = transactions.ToDictionary(t => t.TransactionId);
        var matchedTransactionIds = new HashSet<string>();

        var localOrders = await _orderRepository.ListAsync(new OrdersWithPaymentInDateRangeSpecification(from, to), ct);

        var entries = new List<ReconciliationEntry>();
        foreach (var order in localOrders)
        {
            var payment = order.Payment!;
            var localId = payment.CaptureId ?? payment.AuthorizationId;
            var localAmount = payment.CapturedAmount ?? payment.AuthorizedAmount;
            entries.Add(BuildEntry(localId, localAmount, $"Order {order.Id} ({order.Status})", order.Id, transactionsById, matchedTransactionIds));

            foreach (var refund in payment.Refunds)
            {
                entries.Add(BuildEntry(refund.PayPalRefundId, -refund.Amount, $"Order {order.Id} refund", order.Id, transactionsById, matchedTransactionIds));
            }
        }

        foreach (var txn in transactions)
        {
            if (!matchedTransactionIds.Contains(txn.TransactionId))
            {
                entries.Add(new ReconciliationEntry(txn.TransactionId, txn.Amount, txn.Status, null, null, null, ReconciliationMatchState.PayPalOnly));
            }
        }

        return new ReconciliationReport(from, to, entries);
    }

    private static ReconciliationEntry BuildEntry(string localId, decimal localAmount, string localDescription, int orderId,
        IReadOnlyDictionary<string, PayPalTransactionRecord> transactionsById, HashSet<string> matchedTransactionIds)
    {
        if (transactionsById.TryGetValue(localId, out var txn))
        {
            matchedTransactionIds.Add(localId);
            return new ReconciliationEntry(txn.TransactionId, txn.Amount, txn.Status, orderId, localAmount, localDescription, ReconciliationMatchState.Matched);
        }

        return new ReconciliationEntry(null, null, null, orderId, localAmount, localDescription, ReconciliationMatchState.EShopOnly);
    }
}
