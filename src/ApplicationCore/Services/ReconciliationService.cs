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

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.CheckoutException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpecification(from, to), cancellationToken);

        var rows = new List<ReconciliationRow>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var keys = CollectOrderKeys(order);
            var matches = paypalTransactions
                .Where(tx => Matches(tx, order, keys))
                .ToList();

            if (matches.Count == 0)
            {
                rows.Add(new ReconciliationRow(
                    "eshop_only",
                    order.Id,
                    null,
                    $"ESHOP-{order.Id}",
                    $"ESHOP-{order.Id}",
                    null,
                    order.Status.ToString(),
                    order.CapturedAmount ?? MoneyFormatter.Round(order.Total()),
                    order.CurrencyCode,
                    "eShop has this order/payment but PayPal reported no matching transaction in the range."));
                continue;
            }

            foreach (var match in matches)
            {
                matchedPaypalIds.Add(match.TransactionId);
                rows.Add(new ReconciliationRow(
                    "matched",
                    order.Id,
                    match.TransactionId,
                    match.InvoiceId,
                    match.CustomField,
                    match.EventCode,
                    match.Status,
                    match.Amount,
                    match.CurrencyCode,
                    null));
            }
        }

        foreach (var tx in paypalTransactions.Where(t => !matchedPaypalIds.Contains(t.TransactionId)))
        {
            rows.Add(new ReconciliationRow(
                "paypal_only",
                TryParseOrderId(tx),
                tx.TransactionId,
                tx.InvoiceId,
                tx.CustomField,
                tx.EventCode,
                tx.Status,
                tx.Amount,
                tx.CurrencyCode,
                "PayPal reported this transaction but eShop has no matching order in the range."));
        }

        return new ReconciliationReport(
            from,
            to,
            rows.OrderBy(r => r.PayPalTransactionId).ThenBy(r => r.OrderId).ToList(),
            rows.Count(r => r.Match == "matched"),
            rows.Count(r => r.Match == "paypal_only"),
            rows.Count(r => r.Match == "eshop_only"));
    }

    private static HashSet<string> CollectOrderKeys(Order order)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"ESHOP-{order.Id}",
            order.Id.ToString()
        };

        Add(keys, order.PayPalOrderId);
        Add(keys, order.PayPalAuthorizationId);
        Add(keys, order.PayPalCaptureId);
        foreach (var refund in order.Refunds)
        {
            Add(keys, refund.PayPalRefundId);
        }

        return keys;
    }

    private static void Add(HashSet<string> keys, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(value);
        }
    }

    private static bool Matches(PayPalReportedTransaction tx, Order order, HashSet<string> keys)
    {
        if (!string.IsNullOrEmpty(tx.TransactionId) && keys.Contains(tx.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.PaypalReferenceId) && keys.Contains(tx.PaypalReferenceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.InvoiceId) && keys.Contains(tx.InvoiceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.CustomField) && keys.Contains(tx.CustomField))
        {
            return true;
        }

        return false;
    }

    private static int? TryParseOrderId(PayPalReportedTransaction tx)
    {
        foreach (var candidate in new[] { tx.InvoiceId, tx.CustomField })
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            var value = candidate.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase)
                ? candidate["ESHOP-".Length..]
                : candidate;
            if (int.TryParse(value, out var id))
            {
                return id;
            }
        }

        return null;
    }
}
