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
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPalClient;

    public PaymentReconciliationService(IReadRepository<Order> orderRepository, IPayPalClient payPalClient)
    {
        _orderRepository = orderRepository;
        _payPalClient = payPalClient;
    }

    public async Task<IReadOnlyList<ReconciliationRow>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPalClient.ListAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new PaidOrdersSpecification(), cancellationToken);

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order != null)
            {
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrWhiteSpace(txn.TransactionId))
                {
                    matchedTransactionIds.Add(txn.TransactionId);
                }

                rows.Add(BuildRow("matched", order, txn, "Present in PayPal and eShop."));
            }
            else
            {
                rows.Add(BuildRow("paypal_only", null, txn, "PayPal has this transaction and eShop does not."));
            }
        }

        foreach (var order in orders.Where(o => OrderInRange(o, from, to) && !matchedOrderIds.Contains(o.Id)))
        {
            rows.Add(new ReconciliationRow
            {
                MatchStatus = "eshop_only",
                OrderId = order.Id,
                OrderPaymentStatus = order.PaymentStatus.ToString(),
                PayPalTransactionId = order.PayPalCaptureId ?? order.PayPalAuthorizationId,
                Amount = PaymentFormatting.FormatAmount(order.CapturedAmount ?? order.Total(), order.Currency ?? _payPalClient.Currency),
                Currency = order.Currency ?? _payPalClient.Currency,
                Notes = "eShop has this payment and PayPal's report for the range does not."
            });
        }

        return rows
            .OrderBy(r => r.MatchStatus)
            .ThenBy(r => r.OrderId)
            .ThenBy(r => r.PayPalTransactionId)
            .ToList();
    }

    private static bool OrderInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        return order.OrderDate >= from && order.OrderDate <= to;
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order.PayPalInvoiceId, txn.CustomField)
                || Matches(order.PayPalInvoiceId, txn.InvoiceId)
                || Matches(order.PayPalInvoiceId + "-c", txn.InvoiceId)
                || Matches(order.PayPalOrderId, txn.TransactionId)
                || Matches(order.PayPalOrderId, txn.ReferenceId)
                || Matches(order.PayPalAuthorizationId, txn.TransactionId)
                || Matches(order.PayPalAuthorizationId, txn.ReferenceId)
                || Matches(order.PayPalCaptureId, txn.TransactionId)
                || Matches(order.PayPalCaptureId, txn.ReferenceId)
                || order.Refunds.Any(r => Matches(r.PayPalRefundId, txn.TransactionId) || Matches(r.PayPalRefundId, txn.ReferenceId)))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static ReconciliationRow BuildRow(string status, Order? order, PayPalReportedTransaction txn, string notes)
    {
        return new ReconciliationRow
        {
            MatchStatus = status,
            OrderId = order?.Id,
            OrderPaymentStatus = order?.PaymentStatus.ToString(),
            PayPalTransactionId = txn.TransactionId,
            PayPalReferenceId = txn.ReferenceId,
            PayPalCustomField = txn.CustomField,
            PayPalEventCode = txn.EventCode,
            PayPalStatus = txn.Status,
            Amount = txn.Amount,
            Currency = txn.Currency,
            Notes = notes
        };
    }
}
