using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orders)
    {
        _payPal = payPal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "`to` must be on or after `from`.", "INVALID_DATE_RANGE");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orders.ListAsync(new OrdersWithPaymentActivitySpec(from, to), cancellationToken);
        var eshopRecords = eshopOrders.Select(ToRecord).ToList();

        var matched = new List<ReconciliationMatch>();
        var unmatchedPaypal = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = eshopRecords.FirstOrDefault(o => Matches(o, txn));
            if (order is null)
            {
                unmatchedPaypal.Add(txn);
                continue;
            }

            matched.Add(new ReconciliationMatch(order, txn));
            matchedOrderIds.Add(order.OrderId);
        }

        var unmatchedEshop = eshopRecords.Where(o => !matchedOrderIds.Contains(o.OrderId)).ToList();
        return new ReconciliationReport(from, to, matched, unmatchedPaypal, unmatchedEshop);
    }

    private static EshopPaymentRecord ToRecord(Order order)
    {
        return new EshopPaymentRecord(
            order.Id,
            order.Status.ToString(),
            order.PayPalOrderId,
            order.PayPalInvoiceId,
            order.PayPalAuthorizationId,
            order.PayPalCaptureId,
            order.Refunds.Select(r => r.PayPalRefundId).ToList(),
            order.OrderDate);
    }

    private static bool Matches(EshopPaymentRecord order, PayPalReportedTransaction txn)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, order.PayPalOrderId);
        Add(ids, order.InvoiceId);
        Add(ids, order.AuthorizationId);
        Add(ids, order.CaptureId);
        foreach (var refundId in order.RefundIds)
        {
            Add(ids, refundId);
        }

        return Contains(ids, txn.TransactionId)
               || Contains(ids, txn.PaypalReferenceId)
               || Contains(ids, txn.InvoiceId)
               || Contains(ids, txn.CustomField);
    }

    private static void Add(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static bool Contains(HashSet<string> ids, string? value) =>
        !string.IsNullOrWhiteSpace(value) && ids.Contains(value);
}
