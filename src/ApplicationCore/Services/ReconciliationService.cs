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
    private readonly IPayPalGateway _payPal;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPal, IRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            throw new Exceptions.CheckoutException(400, "Both from and to ISO-8601 date-times are required.");

        var start = FormatPayPalTimestamp(from, "from");
        var end = FormatPayPalTimestamp(to, "to");

        var paypalRows = await _payPal.SearchTransactionsAsync(start, end, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDt);
        DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDt);
        var rangeOrders = orders
            .Where(o => InRange(o.OrderDate, fromDt, toDt) || HasPayPalActivity(o))
            .ToList();

        var matchedPaypal = new HashSet<PayPalTransactionRecord>();
        var rows = new List<ReconciliationRow>();

        foreach (var txn in paypalRows)
        {
            var order = FindOrder(rangeOrders, txn) ?? FindOrder(orders, txn);
            if (order == null)
            {
                rows.Add(new ReconciliationRow(
                    "PayPalOnly",
                    null,
                    null,
                    txn.TransactionId,
                    txn.TransactionStatus,
                    txn.InvoiceId,
                    txn.CustomField,
                    txn.TransactionAmount,
                    null));
                continue;
            }

            matchedPaypal.Add(txn);
            rows.Add(new ReconciliationRow(
                "Matched",
                order.Id,
                order.PaymentStatus,
                txn.TransactionId,
                txn.TransactionStatus,
                txn.InvoiceId,
                txn.CustomField,
                txn.TransactionAmount,
                order.Total()));
        }

        var matchedOrderIds = new HashSet<int>(
            rows.Where(r => r.OrderId.HasValue && r.MatchStatus == "Matched").Select(r => r.OrderId!.Value));

        foreach (var order in rangeOrders)
        {
            if (matchedOrderIds.Contains(order.Id))
                continue;
            if (string.IsNullOrEmpty(order.PayPalOrderId) && string.IsNullOrEmpty(order.AuthorizationId) && string.IsNullOrEmpty(order.CaptureId))
                continue;

            rows.Add(new ReconciliationRow(
                "EshopOnly",
                order.Id,
                order.PaymentStatus,
                order.CaptureId ?? order.AuthorizationId,
                order.CaptureStatus ?? order.AuthorizationStatus,
                order.Id.ToString(CultureInfo.InvariantCulture),
                order.Id.ToString(CultureInfo.InvariantCulture),
                null,
                order.Total()));
        }

        var matched = rows.Count(r => r.MatchStatus == "Matched");
        var paypalOnly = rows.Count(r => r.MatchStatus == "PayPalOnly");
        var eshopOnly = rows.Count(r => r.MatchStatus == "EshopOnly");

        return new ReconciliationReport(
            start,
            end,
            rows,
            paypalRows.Count,
            rangeOrders.Count,
            matched,
            paypalOnly,
            eshopOnly);
    }

    private static string FormatPayPalTimestamp(string input, string name)
    {
        if (!DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new Exceptions.CheckoutException(400, $"{name} must be an ISO-8601 date-time.");
        return parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static Order? FindOrder(IEnumerable<Order> orders, PayPalTransactionRecord txn)
    {
        foreach (var order in orders)
        {
            var id = order.Id.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(txn.InvoiceId) && txn.InvoiceId == id)
                return order;
            if (!string.IsNullOrEmpty(txn.CustomField) && txn.CustomField == id)
                return order;
        }
        return null;
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static bool HasPayPalActivity(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId)
        || !string.IsNullOrEmpty(order.AuthorizationId)
        || !string.IsNullOrEmpty(order.CaptureId);
}
