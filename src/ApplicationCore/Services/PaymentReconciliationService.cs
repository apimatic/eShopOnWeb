using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentsClient _payPal;

    public PaymentReconciliationService(
        IRepository<Order> orderRepository,
        IPayPalPaymentsClient payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentOperationException(400, "`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpec(from, to), cancellationToken);

        // Match only on identifiers this process actually stored for an order.
        // Bare numeric order ids and generic "ESHOP-{id}" prefixes collide across
        // in-memory restarts and other sandbox runs on the same merchant account.
        var eShopByToken = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            Index(eShopByToken, order.PayPalOrderId, order);
            Index(eShopByToken, order.PayPalAuthorizationId, order);
            Index(eShopByToken, order.PayPalCaptureId, order);
            Index(eShopByToken, order.PayPalInvoiceId, order);
            foreach (var refund in order.Refunds)
            {
                Index(eShopByToken, refund.PayPalRefundId, order);
            }
        }

        var matched = new List<MatchedPayment>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            Order? order = null;
            var reason = string.Empty;
            if (TryGet(eShopByToken, txn.TransactionId, out order))
            {
                reason = "transaction_id";
            }
            else if (TryGet(eShopByToken, txn.ReferenceId, out order))
            {
                reason = "paypal_reference_id";
            }
            else if (TryGet(eShopByToken, txn.InvoiceId, out order))
            {
                reason = "invoice_id";
            }
            else if (TryGet(eShopByToken, txn.CustomField, out order))
            {
                reason = "custom_field";
            }

            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matched.Add(new MatchedPayment
            {
                OrderId = order.Id,
                PayPalTransactionId = txn.TransactionId,
                InvoiceId = txn.InvoiceId,
                MatchReason = reason
            });
        }

        var eShopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalActivity(o))
            .Select(o => new EShopPaymentRecord
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                PayPalAuthorizationId = o.PayPalAuthorizationId,
                PayPalCaptureId = o.PayPalCaptureId,
                OrderDate = o.OrderDate
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        };
    }

    private static bool HasPayPalActivity(Order order)
        => !string.IsNullOrEmpty(order.PayPalOrderId)
           || !string.IsNullOrEmpty(order.PayPalAuthorizationId)
           || !string.IsNullOrEmpty(order.PayPalCaptureId);

    private static void Index(Dictionary<string, Order> map, string? key, Order order)
    {
        if (string.IsNullOrWhiteSpace(key) || map.ContainsKey(key))
        {
            return;
        }

        map[key] = order;
    }

    private static bool TryGet(Dictionary<string, Order> map, string? key, out Order? order)
    {
        order = null;
        return !string.IsNullOrWhiteSpace(key) && map.TryGetValue(key, out order);
    }
}
