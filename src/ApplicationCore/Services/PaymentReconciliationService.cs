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
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public PaymentReconciliationService(
        IPaymentGateway paymentGateway,
        IReadRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsInRangeSpecification(from, to), cancellationToken);

        var eshopEntries = BuildEshopEntries(orders);
        var remainingEshop = eshopEntries.ToList();
        var matches = new List<ReconciliationMatch>();

        foreach (var txn in paypalTransactions)
        {
            var match = remainingEshop.FirstOrDefault(e => IsMatch(e, txn));
            if (match != null)
            {
                remainingEshop.Remove(match);
                matches.Add(new ReconciliationMatch(txn, match.OrderId, "matched"));
            }
            else
            {
                var alreadyKnown = eshopEntries.FirstOrDefault(e => IsMatch(e, txn));
                matches.Add(alreadyKnown != null
                    ? new ReconciliationMatch(txn, alreadyKnown.OrderId, "matched")
                    : new ReconciliationMatch(txn, null, "paypal_only"));
            }
        }

        foreach (var leftover in remainingEshop)
        {
            matches.Add(new ReconciliationMatch(null, leftover.OrderId, "eshop_only"));
        }

        var matchedCount = matches.Count(m => m.MatchKind == "matched");
        var paypalOnly = matches.Count(m => m.MatchKind == "paypal_only");
        var eshopOnly = matches.Count(m => m.MatchKind == "eshop_only");

        return new ReconciliationReport(
            from,
            to,
            matches,
            paypalTransactions.Count,
            eshopEntries.Select(e => e.OrderId).Distinct().Count(),
            matchedCount,
            paypalOnly,
            eshopOnly);
    }

    private static List<EshopPaymentEntry> BuildEshopEntries(IEnumerable<Order> orders)
    {
        var entries = new List<EshopPaymentEntry>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(payment.PayPalOrderId))
            {
                entries.Add(new EshopPaymentEntry(order.Id, "paypal_order", payment.PayPalOrderId));
            }

            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                entries.Add(new EshopPaymentEntry(order.Id, "authorization", payment.AuthorizationId));
            }

            if (!string.IsNullOrWhiteSpace(payment.CaptureId))
            {
                entries.Add(new EshopPaymentEntry(order.Id, "capture", payment.CaptureId));
            }

            foreach (var refund in payment.Refunds)
            {
                entries.Add(new EshopPaymentEntry(order.Id, "refund", refund.PayPalRefundId));
            }

            if (!string.IsNullOrWhiteSpace(payment.InvoiceId))
            {
                entries.Add(new EshopPaymentEntry(order.Id, "invoice", payment.InvoiceId));
            }
        }

        return entries;
    }

    private static bool IsMatch(EshopPaymentEntry entry, GatewayTransaction txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.TransactionId) &&
            string.Equals(entry.PayPalId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.ReferenceId) &&
            string.Equals(entry.PayPalId, txn.ReferenceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
            string.Equals(entry.PayPalId, txn.CustomField, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.InvoiceId) &&
            string.Equals(entry.PayPalId, txn.InvoiceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private sealed record EshopPaymentEntry(int OrderId, string Kind, string PayPalId);
}
