using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentValidationException("'to' must be greater than or equal to 'from'.");
        }

        var paypalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = (await _orderRepository.ListAsync(
            new OrdersForReconciliationSpecification(from, to), cancellationToken)).ToList();

        var paypalIds = paypalTransactions
            .SelectMany(tx => new[] { tx.TransactionId, tx.PayPalReferenceId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paypalIds.Count > 0)
        {
            var extra = await _orderRepository.ListAsync(
                new OrdersByPayPalIdentifiersSpecification(paypalIds), cancellationToken);
            foreach (var order in extra)
            {
                if (eshopOrders.All(o => o.Id != order.Id))
                {
                    eshopOrders.Add(order);
                }
            }
        }

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();
        var matchedPaypalTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in paypalTransactions)
        {
            var order = FindMatchingOrder(eshopOrders, tx);
            if (order is null)
            {
                paypalOnly.Add(new ReconciliationPayPalOnly
                {
                    PayPalTransactionId = tx.TransactionId,
                    PayPalReferenceId = tx.PayPalReferenceId,
                    InvoiceId = tx.InvoiceId,
                    CustomField = tx.CustomField,
                    Amount = tx.Amount,
                    Currency = tx.Currency,
                    Status = tx.Status,
                    EventCode = tx.EventCode
                });
                continue;
            }

            matchedOrderIds.Add(order.Id);
            if (!string.IsNullOrWhiteSpace(tx.TransactionId))
            {
                matchedPaypalTxnIds.Add(tx.TransactionId);
            }

            matches.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                PayPalTransactionId = tx.TransactionId,
                PayPalReferenceId = tx.PayPalReferenceId,
                InvoiceId = tx.InvoiceId,
                EshopPaymentId = MatchingEshopId(order, tx),
                Status = tx.Status
            });
        }

        var eshopOnly = eshopOrders
            .Where(o => o.Status != OrderStatus.AwaitingPayment && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEshopOnly
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.Payment.PayPalOrderId,
                AuthorizationId = o.Payment.AuthorizationId,
                CaptureId = o.Payment.CaptureId,
                Amount = o.Total()
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

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction tx)
    {
        foreach (var order in orders)
        {
            if (IdsEqual(tx.TransactionId, order.Payment.CaptureId) ||
                IdsEqual(tx.TransactionId, order.Payment.AuthorizationId) ||
                IdsEqual(tx.TransactionId, order.Payment.PayPalOrderId) ||
                IdsEqual(tx.PayPalReferenceId, order.Payment.CaptureId) ||
                IdsEqual(tx.PayPalReferenceId, order.Payment.AuthorizationId) ||
                IdsEqual(tx.PayPalReferenceId, order.Payment.PayPalOrderId) ||
                order.Refunds.Any(r =>
                    IdsEqual(tx.TransactionId, r.PayPalRefundId) ||
                    IdsEqual(tx.PayPalReferenceId, r.PayPalRefundId)))
            {
                return order;
            }

            var invoice = OrderPaymentService.InvoiceIdFor(order);
            if (!string.IsNullOrWhiteSpace(tx.InvoiceId) &&
                (string.Equals(tx.InvoiceId, invoice, StringComparison.OrdinalIgnoreCase) ||
                 tx.InvoiceId.StartsWith($"ESHOP-{order.Id}-", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tx.InvoiceId, $"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(tx.CustomField) &&
                (string.Equals(tx.CustomField, order.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tx.CustomField, invoice, StringComparison.OrdinalIgnoreCase)))
            {
                return order;
            }
        }

        return null;
    }

    private static string? MatchingEshopId(Order order, PayPalReportedTransaction tx)
    {
        if (IdsEqual(tx.TransactionId, order.Payment.CaptureId) || IdsEqual(tx.PayPalReferenceId, order.Payment.CaptureId))
        {
            return order.Payment.CaptureId;
        }

        if (IdsEqual(tx.TransactionId, order.Payment.AuthorizationId) || IdsEqual(tx.PayPalReferenceId, order.Payment.AuthorizationId))
        {
            return order.Payment.AuthorizationId;
        }

        var refund = order.Refunds.FirstOrDefault(r =>
            IdsEqual(tx.TransactionId, r.PayPalRefundId) || IdsEqual(tx.PayPalReferenceId, r.PayPalRefundId));
        if (refund is not null)
        {
            return refund.PayPalRefundId;
        }

        return order.Payment.PayPalOrderId;
    }

    private static bool IdsEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               !string.IsNullOrWhiteSpace(right) &&
               string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
