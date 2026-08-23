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
    private readonly IPayPalGateway _payPalGateway;

    public PaymentReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway payPalGateway)
    {
        _orderRepository = orderRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var paypalTransactions = await _payPalGateway.ListTransactionsAsync(from, to, cancellationToken);
        var localOrders = await _orderRepository.ListAsync(new OrdersWithPaymentsInRangeSpec(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReportedTransaction>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(localOrders, txn);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matchedPaypalIds.Add(txn.TransactionId);
            matched.Add(new ReconciliationMatch(order.Id, txn.TransactionId, txn.Status, txn.Amount?.Value));
        }

        var localOnly = localOrders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalIdentity(o))
            .Select(o => new ReconciliationLocalPayment(
                o.Id,
                o.Status.ToString(),
                o.Payment.PayPalOrderId,
                o.Payment.AuthorizationId,
                o.Payment.CaptureId))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, localOnly);
    }

    private static bool HasPayPalIdentity(Order order) =>
        !string.IsNullOrEmpty(order.Payment.PayPalOrderId)
        || !string.IsNullOrEmpty(order.Payment.AuthorizationId)
        || !string.IsNullOrEmpty(order.Payment.CaptureId);

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, ReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order.Payment.CaptureId, txn.TransactionId)
                || Matches(order.Payment.AuthorizationId, txn.TransactionId)
                || Matches(order.Payment.PayPalOrderId, txn.TransactionId)
                || Matches(order.Payment.CaptureId, txn.ReferenceId)
                || Matches(order.Payment.AuthorizationId, txn.ReferenceId)
                || Matches(order.Id.ToString(), txn.CustomField)
                || Matches($"ORDER-{order.Id}", txn.InvoiceId)
                || StartsWithInvoice(order.Id, txn.InvoiceId)
                || order.Refunds.Any(r => Matches(r.PayPalRefundId, txn.TransactionId)))
            {
                return order;
            }
        }

        return null;
    }

    private static bool StartsWithInvoice(int orderId, string? invoiceId) =>
        !string.IsNullOrWhiteSpace(invoiceId)
        && invoiceId.StartsWith($"ORDER-{orderId}-", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
