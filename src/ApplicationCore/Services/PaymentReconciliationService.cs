using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalPaymentsClient _payPal;

    public PaymentReconciliationService(
        IReadRepository<Order> orderRepository,
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
            throw Exceptions.OrderPaymentException.BadRequest("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(
            new OrdersForReconciliationSpecification(from, to),
            cancellationToken);

        var ordersInRange = orders
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .ToList();

        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<ReconciliationLine>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindOrder(orders, txn);
            if (order != null)
            {
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrEmpty(txn.TransactionId))
                {
                    matchedTxnIds.Add(txn.TransactionId);
                }

                lines.Add(ToLine("Matched", order, txn));
            }
            else
            {
                lines.Add(ToLine("PayPalOnly", null, txn));
            }
        }

        foreach (var order in ordersInRange)
        {
            if (matchedOrderIds.Contains(order.Id))
            {
                continue;
            }

            if (!HasPaymentActivity(order))
            {
                continue;
            }

            lines.Add(ToLine("EshopOnly", order, null));
        }

        return new ReconciliationReport(
            from,
            to,
            paypalTransactions.Count,
            ordersInRange.Count(HasPaymentActivity),
            lines.Count(l => l.Match == "Matched"),
            lines.Count(l => l.Match == "PayPalOnly"),
            lines.Count(l => l.Match == "EshopOnly"),
            lines);
    }

    private static bool HasPaymentActivity(Order order) =>
        !string.IsNullOrEmpty(order.Payment?.PayPalOrderId)
        || !string.IsNullOrEmpty(order.Payment?.AuthorizationId)
        || !string.IsNullOrEmpty(order.Payment?.CaptureId);

    private static Order? FindOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        var candidates = new[]
        {
            txn.TransactionId,
            txn.PaypalReferenceId,
            txn.InvoiceId,
            txn.CustomField
        }.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        foreach (var order in orders)
        {
            var identifiers = new HashSet<string>(order.PayPalIdentifiers(), StringComparer.OrdinalIgnoreCase);
            if (candidates.Any(c => identifiers.Contains(c!)))
            {
                return order;
            }
        }

        return null;
    }

    private static ReconciliationLine ToLine(string match, Order? order, PayPalReportedTransaction? txn) =>
        new(
            match,
            order?.Id,
            order?.Status.ToString(),
            txn?.TransactionId,
            txn?.PaypalReferenceId,
            txn?.InvoiceId ?? order?.InvoiceId(),
            txn?.EventCode,
            txn?.Status,
            txn?.Amount,
            txn?.FeeAmount,
            txn?.CurrencyCode ?? order?.Payment?.Currency,
            txn?.TransactionDate);
}
