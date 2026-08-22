using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalPaymentGateway _payPal;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPayPalPaymentGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalTransactionRecord>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var order = FindOrder(txn, ordersById);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                matchedTransactionIds.Add(txn.TransactionId);
            }

            matched.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                PayPalTransactionId = txn.TransactionId,
                InvoiceId = txn.InvoiceId,
                OrderStatus = order.Status.ToString(),
                PayPalStatus = txn.Status,
                OrderTotal = order.Total(),
                PayPalAmount = txn.Amount
            });
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Where(o => !string.IsNullOrEmpty(o.Payment.PayPalOrderId) || !string.IsNullOrEmpty(o.Payment.CaptureId))
            .Select(o => new EshopUnmatchedOrder
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                Total = o.Total(),
                PayPalOrderId = o.Payment.PayPalOrderId,
                CaptureId = o.Payment.CaptureId,
                OrderDate = o.OrderDate
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

    private static Order? FindOrder(PayPalTransactionRecord txn, IReadOnlyDictionary<int, Order> ordersById)
    {
        if (TryParseOrderId(txn.CustomField, out var customId) && ordersById.TryGetValue(customId, out var byCustom))
        {
            return byCustom;
        }

        if (TryParseOrderId(txn.InvoiceId, out var invoiceId) && ordersById.TryGetValue(invoiceId, out var byInvoice))
        {
            return byInvoice;
        }

        foreach (var order in ordersById.Values)
        {
            if (IdsEqual(txn.TransactionId, order.Payment.CaptureId)
                || IdsEqual(txn.TransactionId, order.Payment.AuthorizationId)
                || IdsEqual(txn.TransactionId, order.Payment.PayPalOrderId)
                || IdsEqual(txn.PaypalReferenceId, order.Payment.CaptureId)
                || IdsEqual(txn.PaypalReferenceId, order.Payment.AuthorizationId)
                || IdsEqual(txn.PaypalReferenceId, order.Payment.PayPalOrderId))
            {
                return order;
            }
        }

        return null;
    }

    private static bool TryParseOrderId(string? value, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value;
        const string prefix = "ESHOP-";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[prefix.Length..];
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId);
    }

    private static bool IdsEqual(string? left, string? right)
    {
        return !string.IsNullOrEmpty(left)
            && !string.IsNullOrEmpty(right)
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
