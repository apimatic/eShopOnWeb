using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentProcessor _payments;

    public ReconciliationService(IRepository<Order> orderRepository, IPaymentProcessor payments)
    {
        _orderRepository = orderRepository;
        _payments = payments;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.");

        var paypal = await _payments.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), ct);

        var matched = new List<ReconciliationRow>();
        var paypalOnly = new List<ProviderTransaction>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypal)
        {
            var order = Match(orders, txn);
            if (order == null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            if (!string.IsNullOrEmpty(txn.TransactionId))
                matchedTxnIds.Add(txn.TransactionId);

            matched.Add(new ReconciliationRow
            {
                OrderId = order.Id,
                PayPalTransactionId = txn.TransactionId,
                MatchReason = DescribeMatch(order, txn)
            });
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && o.Payment.HasHold)
            .Select(o => new ReconciliationOrderSummary
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                PayPalOrderId = o.Payment.PayPalOrderId,
                AuthorizationId = o.Payment.AuthorizationId,
                CaptureId = o.Payment.CaptureId,
                Total = o.Total()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static Order? Match(IReadOnlyList<Order> orders, ProviderTransaction txn)
    {
        if (TryParseOrderId(txn.CustomField, out var customId))
        {
            var byCustom = orders.FirstOrDefault(o => o.Id == customId);
            if (byCustom != null)
                return byCustom;
        }

        if (TryParseOrderId(txn.InvoiceId, out var invoiceId))
        {
            var byInvoice = orders.FirstOrDefault(o => o.Id == invoiceId);
            if (byInvoice != null)
                return byInvoice;
        }

        return orders.FirstOrDefault(o =>
            (!string.IsNullOrEmpty(txn.TransactionId) &&
             (string.Equals(o.Payment.CaptureId, txn.TransactionId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.Payment.AuthorizationId, txn.TransactionId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.Payment.PayPalOrderId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrEmpty(txn.PaypalReferenceId) &&
             (string.Equals(o.Payment.CaptureId, txn.PaypalReferenceId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.Payment.AuthorizationId, txn.PaypalReferenceId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.Payment.PayPalOrderId, txn.PaypalReferenceId, StringComparison.OrdinalIgnoreCase))));
    }

    private static bool TryParseOrderId(string? value, out int orderId) =>
        int.TryParse(value, out orderId);

    private static string DescribeMatch(Order order, ProviderTransaction txn)
    {
        if (TryParseOrderId(txn.CustomField, out var customId) && customId == order.Id)
            return "custom_id";
        if (TryParseOrderId(txn.InvoiceId, out var invoiceId) && invoiceId == order.Id)
            return "invoice_id";
        return "paypal_id";
    }
}
