using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const string Matched = "Matched";
    private const string AmountMismatch = "AmountMismatch";
    private const string PayPalOnly = "PayPalOnly";
    private const string EShopOnly = "EShopOnly";

    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> GetReconciliationReportAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivityInRangeSpecification(from, to));

        // Every PayPal-side id this app could ever have produced (authorization, capture, refund) mapped back to its order.
        var idToOrder = new Dictionary<string, Order>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;

            if (payment.CaptureId is not null) idToOrder.TryAdd(payment.CaptureId, order);
            if (payment.AuthorizationId is not null) idToOrder.TryAdd(payment.AuthorizationId, order);
            foreach (var refund in payment.Refunds)
            {
                idToOrder.TryAdd(refund.PayPalRefundId, order);
            }
        }

        var matchedIds = new HashSet<string>();
        var entries = new List<ReconciliationEntry>();

        foreach (var txn in transactions)
        {
            if (idToOrder.TryGetValue(txn.TransactionId, out var order))
            {
                matchedIds.Add(txn.TransactionId);
                var eshopAmount = LocalAmountFor(order.Payment!, txn.TransactionId);
                var matchStatus = eshopAmount.HasValue && txn.Amount.HasValue && eshopAmount.Value != txn.Amount.Value
                    ? AmountMismatch
                    : Matched;

                entries.Add(new ReconciliationEntry(txn.TransactionId, order.Id, txn.Amount, eshopAmount, txn.Status, matchStatus));
            }
            else
            {
                entries.Add(new ReconciliationEntry(txn.TransactionId, null, txn.Amount, null, txn.Status, PayPalOnly));
            }
        }

        foreach (var (id, order) in idToOrder)
        {
            if (matchedIds.Contains(id)) continue;

            var eshopAmount = LocalAmountFor(order.Payment!, id);
            entries.Add(new ReconciliationEntry(id, order.Id, null, eshopAmount, null, EShopOnly));
        }

        return entries;
    }

    private static decimal? LocalAmountFor(OrderPayment payment, string payPalId)
    {
        if (payment.CaptureId == payPalId) return payment.CapturedAmount;
        if (payment.AuthorizationId == payPalId) return payment.AuthorizedAmount;
        return payment.Refunds.FirstOrDefault(r => r.PayPalRefundId == payPalId)?.Amount;
    }
}
