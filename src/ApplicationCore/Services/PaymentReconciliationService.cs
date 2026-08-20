using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public PaymentReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.");
        }

        var paypalTxns = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var matches = new List<ReconciliationRow>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var order in orders)
        {
            foreach (var txn in paypalTxns.Where(t => RelatesTo(order, t)))
            {
                matchedTxnIds.Add(txn.TransactionId);
                matchedOrderIds.Add(order.Id);
                matches.Add(new ReconciliationRow
                {
                    OrderId = order.Id,
                    OrderStatus = order.Status.ToString(),
                    PayPalTransactionId = txn.TransactionId,
                    PayPalCaptureId = order.Payment.CaptureId,
                    PayPalAuthorizationId = order.Payment.AuthorizationId,
                    PaypalAmount = txn.Amount,
                    EshopAmount = order.Payment.CapturedAmount ?? order.Total(),
                    MatchReason = DescribeMatch(order, txn)
                });
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PaypalOnly = paypalTxns.Where(t => !matchedTxnIds.Contains(t.TransactionId)).ToList(),
            EshopOnly = orders
                .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalState(o) && InRange(o, from, to))
                .Select(o => new ReconciliationEshopOnly
                {
                    OrderId = o.Id,
                    OrderStatus = o.Status.ToString(),
                    PayPalOrderId = o.Payment.PayPalOrderId,
                    AuthorizationId = o.Payment.AuthorizationId,
                    CaptureId = o.Payment.CaptureId,
                    Total = o.Total()
                })
                .ToList()
        };
    }

    private static bool HasPayPalState(Order order) =>
        !string.IsNullOrEmpty(order.Payment.PayPalOrderId)
        || !string.IsNullOrEmpty(order.Payment.AuthorizationId)
        || !string.IsNullOrEmpty(order.Payment.CaptureId);

    private static bool InRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var timestamp = order.Payment.AuthorizationCreatedAt ?? order.OrderDate;
        return timestamp >= from && timestamp <= to;
    }

    private static bool RelatesTo(Order order, PayPalReportedTransaction txn)
    {
        var invoice = $"ESHOP-{order.Id}";
        if (!string.IsNullOrEmpty(txn.InvoiceId) && string.Equals(txn.InvoiceId, invoice, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.CustomField) && string.Equals(txn.CustomField, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IdsOf(order).Any(id =>
            string.Equals(id, txn.TransactionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, txn.PaypalReferenceId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> IdsOf(Order order)
    {
        if (!string.IsNullOrEmpty(order.Payment.PayPalOrderId)) yield return order.Payment.PayPalOrderId;
        if (!string.IsNullOrEmpty(order.Payment.AuthorizationId)) yield return order.Payment.AuthorizationId;
        if (!string.IsNullOrEmpty(order.Payment.CaptureId)) yield return order.Payment.CaptureId;
        foreach (var refund in order.Refunds)
        {
            yield return refund.PayPalRefundId;
        }
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (string.Equals(txn.InvoiceId, $"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase))
        {
            return "invoice_id";
        }

        if (string.Equals(txn.CustomField, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return "custom_field";
        }

        return "paypal_id";
    }
}
