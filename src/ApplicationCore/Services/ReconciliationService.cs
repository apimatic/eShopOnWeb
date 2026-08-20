using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
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
            throw new Exceptions.PaymentException(400, "`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new AllPaidOrdersSpecification(), cancellationToken);

        var eShopRecords = orders
            .Where(o => o.Payment != null && IsInRange(o, from, to))
            .Select(ToRecord)
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var unmatchedPayPal = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order != null)
            {
                matched.Add(new ReconciliationMatch
                {
                    EShop = ToRecord(order),
                    PayPal = txn
                });
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrEmpty(txn.TransactionId))
                {
                    matchedPayPalIds.Add(txn.TransactionId);
                }
            }
            else
            {
                unmatchedPayPal.Add(txn);
            }
        }

        var eShopOnly = eShopRecords.Where(r => !matchedOrderIds.Contains(r.OrderId)).ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = unmatchedPayPal,
            EShopOnly = eShopOnly
        };
    }

    private static bool IsInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
        {
            return true;
        }

        if (order.Payment?.AuthorizedAt >= from && order.Payment.AuthorizedAt <= to)
        {
            return true;
        }

        return order.PaymentRefunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (order.Payment == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(txn.InvoiceId)
                && !string.IsNullOrEmpty(order.Payment.InvoiceId)
                && string.Equals(txn.InvoiceId, order.Payment.InvoiceId, StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (!string.IsNullOrEmpty(txn.PayPalReferenceId)
                && (string.Equals(txn.PayPalReferenceId, order.Payment.PayPalOrderId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(txn.PayPalReferenceId, order.Payment.AuthorizationId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(txn.PayPalReferenceId, order.Payment.CaptureId, StringComparison.OrdinalIgnoreCase)
                    || order.PaymentRefunds.Any(r => string.Equals(r.PayPalRefundId, txn.PayPalReferenceId, StringComparison.OrdinalIgnoreCase))))
            {
                return order;
            }

            if (!string.IsNullOrEmpty(txn.TransactionId)
                && (string.Equals(txn.TransactionId, order.Payment.AuthorizationId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(txn.TransactionId, order.Payment.OriginalAuthorizationId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(txn.TransactionId, order.Payment.CaptureId, StringComparison.OrdinalIgnoreCase)
                    || order.PaymentRefunds.Any(r => string.Equals(r.PayPalRefundId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))))
            {
                return order;
            }
        }

        return null;
    }

    private static EShopPaymentRecord ToRecord(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        PayPalOrderId = order.Payment?.PayPalOrderId,
        AuthorizationId = order.Payment?.AuthorizationId,
        CaptureId = order.Payment?.CaptureId,
        RefundIds = order.PaymentRefunds.Select(r => r.PayPalRefundId).ToList(),
        Total = order.Total(),
        OrderDate = order.OrderDate
    };
}
