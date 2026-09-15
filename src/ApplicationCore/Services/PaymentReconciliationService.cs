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

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentReconciliationService(IRepository<Order> orderRepository, IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.", 400);
        }

        var paypal = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var matched = new List<ReconciliationRow>();
        var unmatchedPaypal = new List<ProcessorTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypal)
        {
            var order = FindOrder(orders, txn);
            if (order is null)
            {
                unmatchedPaypal.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matched.Add(new ReconciliationRow { OrderId = order.Id, Transaction = txn });
        }

        var eshopOnly = orders
            .Where(o => HasProcessorState(o) && InRange(o, from, to) && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationOrderSummary
            {
                OrderId = o.Id,
                Status = o.PaymentStatus.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = unmatchedPaypal,
            EShopOnly = eshopOnly
        };
    }

    private static bool HasProcessorState(Order order)
    {
        return !string.IsNullOrEmpty(order.PayPalOrderId)
            || !string.IsNullOrEmpty(order.AuthorizationId)
            || !string.IsNullOrEmpty(order.CaptureId)
            || order.Refunds.Count > 0;
    }

    private static bool InRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
        {
            return true;
        }

        if (order.OriginalAuthorizationTime is DateTimeOffset authorized
            && authorized >= from && authorized <= to)
        {
            return true;
        }

        return order.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }

    private static Order? FindOrder(IReadOnlyList<Order> orders, ProcessorTransaction txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order, txn))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(Order order, ProcessorTransaction txn)
    {
        var orderId = order.Id.ToString();
        if (string.Equals(txn.CustomField, order.PaymentReference, StringComparison.Ordinal)
            || string.Equals(txn.CustomField, orderId, StringComparison.Ordinal)
            || string.Equals(txn.InvoiceId, order.PaymentReference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(txn.InvoiceId, $"eShop-{order.PaymentReference}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(txn.InvoiceId, orderId, StringComparison.Ordinal))
        {
            return true;
        }

        return IdsEqual(txn.TransactionId, order.PayPalOrderId)
            || IdsEqual(txn.TransactionId, order.AuthorizationId)
            || IdsEqual(txn.TransactionId, order.CaptureId)
            || IdsEqual(txn.PaypalReferenceId, order.PayPalOrderId)
            || IdsEqual(txn.PaypalReferenceId, order.AuthorizationId)
            || IdsEqual(txn.PaypalReferenceId, order.CaptureId)
            || order.Refunds.Any(r =>
                IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.PaypalReferenceId, r.PayPalRefundId));
    }

    private static bool IdsEqual(string? left, string? right)
    {
        return !string.IsNullOrEmpty(left)
            && !string.IsNullOrEmpty(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
