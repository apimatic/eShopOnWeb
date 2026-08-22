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
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentException("'to' must be on or after 'from'.", 400);
        }

        var paypal = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var eshop = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypal)
        {
            var order = MatchOrder(eshop, txn);

            if (order is null)
            {
                paypalOnly.Add(new ReconciliationPayPalOnly(
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.CustomField,
                    txn.Status,
                    txn.Amount));
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matches.Add(new ReconciliationMatch(
                order.Id,
                txn.TransactionId,
                txn.InvoiceId ?? txn.CustomField,
                order.Status.ToString(),
                txn.Status,
                txn.Amount,
                order.CapturedAmount));
        }

        var eshopOnly = eshop
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaymentFingerprint(o))
            .Select(o => new ReconciliationEshopOnly(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.AuthorizationId,
                o.CaptureId,
                o.Total()))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eshopOnly);
    }

    private static Order? MatchOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId))
        {
            var byInvoice = orders.FirstOrDefault(o =>
                string.Equals(o.PayPalInvoiceId, txn.InvoiceId, StringComparison.OrdinalIgnoreCase));
            if (byInvoice is not null)
            {
                return byInvoice;
            }
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField))
        {
            return orders.FirstOrDefault(o =>
                string.Equals(o.PayPalInvoiceId, txn.CustomField, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(OrderPaymentService.UniqueInvoiceId(o), txn.CustomField, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool HasPaymentFingerprint(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId) ||
        !string.IsNullOrEmpty(order.AuthorizationId) ||
        !string.IsNullOrEmpty(order.CaptureId);
}
