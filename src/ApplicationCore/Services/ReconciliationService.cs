using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPalClient;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalClient payPalClient)
    {
        _orderRepository = orderRepository;
        _payPalClient = payPalClient;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to)
    {
        // PayPal's own record of transactions across the whole range (paginated + chunked).
        var transactions = await _payPalClient.ListTransactionsAsync(from, to);

        // eShop's orders in the same range that have reached PayPal (authorized or beyond).
        var orders = (await _orderRepository.ListAsync(new OrdersByDateRangeSpecification(from, to)))
            .Where(o => o.Payment is not null)
            .ToList();

        // Index PayPal transactions by every id we might have stored, so a match is found whether
        // reporting surfaces the reference (invoice_id), the PayPal order id, or a capture id.
        var txnByKey = new Dictionary<string, List<PayPalTransaction>>(StringComparer.OrdinalIgnoreCase);
        void Index(string? key, PayPalTransaction t)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!txnByKey.TryGetValue(key, out var list))
                txnByKey[key] = list = new List<PayPalTransaction>();
            list.Add(t);
        }
        foreach (var t in transactions)
        {
            Index(t.InvoiceId, t);
            Index(t.TransactionId, t);
            Index(t.ReferenceId, t);
        }

        var matched = new List<ReconciliationMatch>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inEShopNotInPayPal = new List<ReconciliationOrder>();

        foreach (var order in orders)
        {
            var view = ToReconciliationOrder(order);
            var found = new List<PayPalTransaction>();

            foreach (var key in CorrelationKeys(order))
            {
                if (txnByKey.TryGetValue(key, out var list))
                    found.AddRange(list);
            }
            found = found.GroupBy(t => t.TransactionId).Select(g => g.First()).ToList();

            if (found.Count > 0)
            {
                matched.Add(new ReconciliationMatch(view, found));
                foreach (var t in found) matchedTxnIds.Add(t.TransactionId);
            }
            else
            {
                inEShopNotInPayPal.Add(view);
            }
        }

        var inPayPalNotInEShop = transactions.Where(t => !matchedTxnIds.Contains(t.TransactionId)).ToList();

        return new ReconciliationReport(
            from,
            to,
            transactions.Count,
            orders.Count,
            matched,
            inPayPalNotInEShop,
            inEShopNotInPayPal);
    }

    private static IEnumerable<string> CorrelationKeys(Order order)
    {
        if (order.Payment is null) yield break;
        if (!string.IsNullOrWhiteSpace(order.Payment.Reference)) yield return order.Payment.Reference;
        if (!string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId)) yield return order.Payment.PayPalOrderId;
        if (!string.IsNullOrWhiteSpace(order.Payment.CaptureId)) yield return order.Payment.CaptureId!;
        if (!string.IsNullOrWhiteSpace(order.Payment.AuthorizationId)) yield return order.Payment.AuthorizationId;
    }

    private static ReconciliationOrder ToReconciliationOrder(Order order) => new(
        order.Id,
        order.Payment?.Reference,
        order.BuyerId,
        order.Total(),
        order.PaymentStatus.ToString(),
        order.Payment?.PayPalOrderId,
        order.Payment?.CaptureId,
        order.OrderDate);
}
