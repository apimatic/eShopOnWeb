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
    private readonly IPayPalGateway _paypal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalGateway paypal, IReadRepository<Order> orders)
    {
        _paypal = paypal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            (from, to) = (to, from);

        var paypalRows = await _paypal.SearchTransactionsAsync(from, to, ct);
        var orders = await _orders.ListAsync(new OrdersWithPaymentsSpecification(), ct);

        var matchedOrderIds = new HashSet<int>();
        var items = new List<ReconciliationMatch>();

        foreach (var txn in paypalRows)
        {
            var order = MatchOrder(orders, txn);

            if (order is not null)
                matchedOrderIds.Add(order.Id);

            items.Add(new ReconciliationMatch
            {
                PayPal = txn,
                OrderId = order?.Id,
                EshopPaymentStatus = order?.PaymentStatus.ToString(),
                Match = order is null ? "paypal_only" : "matched"
            });
        }

        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id)) continue;
            if (!InRange(order, from, to)) continue;
            items.Add(new ReconciliationMatch
            {
                PayPal = null,
                OrderId = order.Id,
                EshopPaymentStatus = order.PaymentStatus.ToString(),
                Match = "eshop_only"
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Items = items
        };
    }

    private static Order? MatchOrder(IReadOnlyList<Order> orders, PayPalTransactionRecord txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId))
        {
            var byInvoice = orders.FirstOrDefault(o =>
                !string.IsNullOrEmpty(o.PaymentIdempotencyKey) &&
                txn.InvoiceId.Contains(o.PaymentIdempotencyKey, StringComparison.OrdinalIgnoreCase));
            if (byInvoice is not null) return byInvoice;
        }

        return MatchByReference(orders, txn);
    }

    private static Order? MatchByReference(IReadOnlyList<Order> orders, PayPalTransactionRecord txn)
    {
        if (string.IsNullOrWhiteSpace(txn.PaypalReferenceId) && string.IsNullOrWhiteSpace(txn.TransactionId))
            return null;

        return orders.FirstOrDefault(o =>
            (!string.IsNullOrEmpty(txn.PaypalReferenceId) &&
                (o.PayPalOrderId == txn.PaypalReferenceId
                 || o.PayPalAuthorizationId == txn.PaypalReferenceId
                 || o.PayPalCaptureId == txn.PaypalReferenceId
                 || o.Refunds.Any(r => r.PayPalRefundId == txn.PaypalReferenceId)))
            || (!string.IsNullOrEmpty(txn.TransactionId) &&
                (o.PayPalOrderId == txn.TransactionId
                 || o.PayPalAuthorizationId == txn.TransactionId
                 || o.PayPalCaptureId == txn.TransactionId
                 || o.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId))));
    }

    private static bool InRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        return order.OrderDate >= from && order.OrderDate <= to;
    }
}
