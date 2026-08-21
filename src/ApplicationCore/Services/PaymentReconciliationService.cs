using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalPaymentsGateway _payPal;

    public PaymentReconciliationService(
        IReadRepository<Order> orderRepository,
        IPayPalPaymentsGateway payPal)
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
            throw new Exceptions.PaymentException("`to` must be on or after `from`.");
        }

        var payPalTransactions = await _payPal.ListAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivityInRangeSpec(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in payPalTransactions)
        {
            var order = FindMatchingOrder(transaction, orders);
            if (order is null)
            {
                payPalOnly.Add(transaction);
                continue;
            }

            matched.Add(new ReconciliationMatch(transaction, order.Id));
            matchedOrderIds.Add(order.Id);
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalState(o))
            .Select(o => new EshopReconciliationEntry(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.PayPalAuthorizationId,
                o.PayPalCaptureId))
            .ToList();

        return new ReconciliationReport(from, to, payPalTransactions, matched, payPalOnly, eshopOnly);
    }

    private static bool HasPayPalState(Order order)
    {
        return !string.IsNullOrEmpty(order.PayPalOrderId) ||
               !string.IsNullOrEmpty(order.PayPalAuthorizationId) ||
               !string.IsNullOrEmpty(order.PayPalCaptureId) ||
               order.Refunds.Count > 0;
    }

    private static Order? FindMatchingOrder(PayPalReportedTransaction transaction, IReadOnlyList<Order> orders)
    {
        foreach (var order in orders)
        {
            if (Matches(transaction, order))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(PayPalReportedTransaction transaction, Order order)
    {
        if (IdsEqual(transaction.InvoiceId, order.PayPalInvoiceId) ||
            IdsEqual(transaction.CustomField, order.PayPalCustomId) ||
            IdsEqual(transaction.CustomField, order.PayPalInvoiceId))
        {
            return true;
        }

        return IdsEqual(transaction.TransactionId, order.PayPalOrderId) ||
               IdsEqual(transaction.TransactionId, order.PayPalAuthorizationId) ||
               IdsEqual(transaction.TransactionId, order.PayPalCaptureId) ||
               IdsEqual(transaction.ReferenceId, order.PayPalOrderId) ||
               IdsEqual(transaction.ReferenceId, order.PayPalAuthorizationId) ||
               IdsEqual(transaction.ReferenceId, order.PayPalCaptureId) ||
               order.Refunds.Any(r =>
                   IdsEqual(transaction.TransactionId, r.PayPalRefundId) ||
                   IdsEqual(transaction.ReferenceId, r.PayPalRefundId));
    }

    private static bool IdsEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
