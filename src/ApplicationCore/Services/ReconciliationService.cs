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

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new AllOrdersWithPaymentSpec(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order is null)
            {
                continue;
            }

            matches.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                OrderStatus = order.Status.ToString(),
                PayPalTransaction = txn
            });

            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                matchedTransactionIds.Add(txn.TransactionId);
            }

            matchedOrderIds.Add(order.Id);
        }

        var paypalOnly = paypalTransactions
            .Where(t => string.IsNullOrEmpty(t.TransactionId) || !matchedTransactionIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasActivityInRange(o, from, to))
            .Select(o => new EshopUnmatchedPayment
            {
                OrderId = o.Id,
                OrderStatus = o.Status.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.PayPalAuthorizationId,
                CaptureId = o.PayPalCaptureId,
                RefundIds = o.Refunds.Select(r => r.PayPalRefundId).ToList(),
                OrderDate = o.OrderDate,
                Total = o.Total()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, GatewayTransaction txn)
    {
        foreach (var order in orders)
        {
            if (IdsEqual(txn.TransactionId, order.PayPalCaptureId)
                || IdsEqual(txn.TransactionId, order.PayPalAuthorizationId)
                || IdsEqual(txn.TransactionId, order.PayPalOrderId)
                || order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId))
                || IdsEqual(txn.ReferenceId, order.PayPalOrderId)
                || IdsEqual(txn.ReferenceId, order.PayPalCaptureId)
                || IdsEqual(txn.ReferenceId, order.PayPalAuthorizationId)
                || IdsEqual(txn.InvoiceId, $"ESHOP-{order.Id}-{order.OrderDate.UtcTicks}")
                || IdsEqual(txn.CustomField, $"{order.Id}-{order.OrderDate.UtcTicks}"))
            {
                return order;
            }
        }

        return null;
    }

    private static bool HasActivityInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
        {
            return true;
        }

        if (order.PayPalAuthorizationCreated is not null
            && order.PayPalAuthorizationCreated >= from
            && order.PayPalAuthorizationCreated <= to)
        {
            return true;
        }

        return order.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left)
        && !string.IsNullOrEmpty(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
