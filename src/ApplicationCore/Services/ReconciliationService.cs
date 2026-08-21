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
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _gateway;

    public ReconciliationService(IRepository<Order> orderRepository, IPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    public async Task<IReadOnlyList<ReconciliationLine>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException(400, "'to' must be on or after 'from'.");
        }

        var paypalRows = await _gateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsInRangeSpec(from, to), ct);

        var ordersById = orders.ToDictionary(o => o.Id.ToString(CultureInfo.InvariantCulture));
        var paypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalRows)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                paypalIds.Add(txn.TransactionId);
            }

            Order? order = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId)
                && ordersById.TryGetValue(txn.InvoiceId, out var byInvoice))
            {
                order = byInvoice;
            }
            else
            {
                order = orders.FirstOrDefault(o => MatchesPayPalIds(o, txn));
            }

            if (order != null)
            {
                matchedOrderIds.Add(order.Id);
                lines.Add(new ReconciliationLine(
                    order.Id.ToString(CultureInfo.InvariantCulture),
                    txn.TransactionId,
                    "matched",
                    txn.InvoiceId,
                    txn.Amount,
                    txn.Status,
                    null));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    null,
                    txn.TransactionId,
                    "paypal-only",
                    txn.InvoiceId,
                    txn.Amount,
                    txn.Status,
                    "PayPal has this transaction but no matching eShop order was found."));
            }
        }

        foreach (var order in orders.Where(o =>
                     !string.IsNullOrEmpty(o.PayPalOrderId)
                     && o.OrderDate >= from
                     && o.OrderDate <= to
                     && !matchedOrderIds.Contains(o.Id)))
        {
            var knownIds = new[] { order.PayPalOrderId, order.AuthorizationId, order.CaptureId }
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>();
            var seen = knownIds.Any(id => paypalIds.Contains(id));
            if (seen)
            {
                continue;
            }

            lines.Add(new ReconciliationLine(
                order.Id.ToString(CultureInfo.InvariantCulture),
                order.CaptureId ?? order.AuthorizationId ?? order.PayPalOrderId,
                "eshop-only",
                order.Id.ToString(CultureInfo.InvariantCulture),
                PayPalMoney.Format(order.Total(), order.Currency ?? "USD"),
                order.PaymentStatus.ToString(),
                "eShop has this payment but PayPal's report for the range does not (reporting can lag live activity)."));
        }

        return lines;
    }

    private static bool MatchesPayPalIds(Order order, PayPalTransactionRecord txn)
    {
        return IdsEqual(order.PayPalOrderId, txn.TransactionId)
            || IdsEqual(order.PayPalOrderId, txn.PaypalReferenceId)
            || IdsEqual(order.AuthorizationId, txn.TransactionId)
            || IdsEqual(order.AuthorizationId, txn.PaypalReferenceId)
            || IdsEqual(order.CaptureId, txn.TransactionId)
            || IdsEqual(order.CaptureId, txn.PaypalReferenceId)
            || order.Refunds.Any(r =>
                IdsEqual(r.PayPalRefundId, txn.TransactionId)
                || IdsEqual(r.PayPalRefundId, txn.PaypalReferenceId));
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left)
        && !string.IsNullOrEmpty(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
