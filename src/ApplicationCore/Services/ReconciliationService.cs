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
    private readonly IRepository<Order> _orders;
    private readonly IPayPalPaymentGateway _payPal;

    public ReconciliationService(IRepository<Order> orders, IPayPalPaymentGateway payPal)
    {
        _orders = orders;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new Exceptions.CheckoutException("'to' must be on or after 'from'.", 400);
        }

        var paypal = await _payPal.SearchTransactionsAsync(from, to, ct);
        var orders = await _orders.ListAsync(new OrdersInDateRangeSpec(from, to), ct);

        var eShop = orders.Select(o => new EShopPaymentRecord(
            o.Id,
            o.BuyerId,
            o.PaymentStatus.ToString(),
            o.PayPalOrderId,
            o.PayPalAuthorizationId,
            o.PayPalCaptureId,
            o.Total(),
            o.CapturedAmount,
            o.OrderDate)).ToList();

        var byOrderId = new Dictionary<string, Order>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            byOrderId[order.Id.ToString()] = order;
            if (!string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                byOrderId[order.PayPalCaptureId] = order;
            }

            if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                byOrderId[order.PayPalAuthorizationId] = order;
            }

            foreach (var refund in order.Refunds)
            {
                byOrderId[refund.PayPalRefundId] = order;
            }
        }

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalTransactionRecord>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypal)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(txn.CustomField) && byOrderId.TryGetValue(txn.CustomField, out var byCustom))
            {
                order = byCustom;
            }
            else if (!string.IsNullOrEmpty(txn.InvoiceId))
            {
                var invoice = txn.InvoiceId;
                if (byOrderId.TryGetValue(invoice, out var byInvoiceExact))
                {
                    order = byInvoiceExact;
                }
                else
                {
                    foreach (var key in byOrderId.Keys)
                    {
                        if (invoice.StartsWith($"eshop-{key}-", StringComparison.Ordinal)
                            || invoice.StartsWith($"eshop-cap-{key}-", StringComparison.Ordinal)
                            || invoice.Contains($"-{key}-", StringComparison.Ordinal))
                        {
                            order = byOrderId[key];
                            break;
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(txn.TransactionId) && byOrderId.TryGetValue(txn.TransactionId, out var byTxn))
            {
                order = byTxn;
            }
            else if (!string.IsNullOrEmpty(txn.PaypalReferenceId) && byOrderId.TryGetValue(txn.PaypalReferenceId, out var byRef))
            {
                order = byRef;
            }

            if (order is null)
            {
                paypalOnly.Add(txn);
            }
            else
            {
                matched.Add(new ReconciliationMatch(txn, order.Id));
                matchedOrderIds.Add(order.Id);
            }
        }

        var eShopOnly = eShop.Where(e => !matchedOrderIds.Contains(e.OrderId)).ToList();

        return new ReconciliationReport(from, to, paypal, eShop, matched, paypalOnly, eShopOnly);
    }
}
