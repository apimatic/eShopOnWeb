using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orders)
    {
        _payPal = payPal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.", 400);
        }

        var paypalTxns = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orders.ListAsync(new OrdersWithPaymentByDateRangeSpec(from, to));

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTxns)
        {
            var order = MatchOrder(orders, txn);
            if (order != null)
            {
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrEmpty(txn.TransactionId))
                {
                    matchedTxnIds.Add(txn.TransactionId);
                }

                rows.Add(new ReconciliationRow(
                    "matched",
                    order.Id,
                    txn.TransactionId,
                    order.Total(),
                    txn.Amount,
                    txn.Currency ?? order.Currency,
                    txn.Status,
                    order.PaymentStatus.ToString(),
                    txn.Timestamp,
                    null));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    "paypal_only",
                    ParseOrderId(txn),
                    txn.TransactionId,
                    null,
                    txn.Amount,
                    txn.Currency,
                    txn.Status,
                    null,
                    txn.Timestamp,
                    "PayPal has this transaction; no matching eShop order was found in the range."));
            }
        }

        foreach (var order in orders.Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalState(o)))
        {
            rows.Add(new ReconciliationRow(
                "eshop_only",
                order.Id,
                order.PayPalCaptureId ?? order.PayPalAuthorizationId ?? order.PayPalOrderId,
                order.Total(),
                null,
                order.Currency,
                null,
                order.PaymentStatus.ToString(),
                order.OrderDate.ToString("o", CultureInfo.InvariantCulture),
                "eShop has this payment; PayPal reporting did not return a matching transaction in the range (reporting can lag live activity)."));
        }

        return new ReconciliationReport(from, to, rows);
    }

    private static bool HasPayPalState(Order order)
    {
        return !string.IsNullOrEmpty(order.PayPalOrderId)
            || !string.IsNullOrEmpty(order.PayPalAuthorizationId)
            || !string.IsNullOrEmpty(order.PayPalCaptureId);
    }

    private static Order? MatchOrder(IReadOnlyList<Order> orders, PayPalTransactionRecord txn)
    {
        var byCustom = TryParseId(txn.CustomField);
        if (byCustom.HasValue)
        {
            var match = orders.FirstOrDefault(o => o.Id == byCustom.Value);
            if (match != null)
            {
                return match;
            }
        }

        var byInvoice = TryParseId(txn.InvoiceId);
        if (byInvoice.HasValue)
        {
            var match = orders.FirstOrDefault(o => o.Id == byInvoice.Value);
            if (match != null)
            {
                return match;
            }
        }

        if (!string.IsNullOrEmpty(txn.PaypalReferenceId)
            && string.Equals(txn.PaypalReferenceIdType, "ODR", StringComparison.OrdinalIgnoreCase))
        {
            var match = orders.FirstOrDefault(o => o.PayPalOrderId == txn.PaypalReferenceId);
            if (match != null)
            {
                return match;
            }
        }

        if (!string.IsNullOrEmpty(txn.TransactionId))
        {
            return orders.FirstOrDefault(o =>
                o.PayPalCaptureId == txn.TransactionId
                || o.PayPalAuthorizationId == txn.TransactionId
                || o.PayPalOrderId == txn.TransactionId
                || o.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId));
        }

        return null;
    }

    private static int? ParseOrderId(PayPalTransactionRecord txn)
    {
        return TryParseId(txn.CustomField) ?? TryParseId(txn.InvoiceId);
    }

    private static int? TryParseId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = value;
        if (digits.StartsWith("o", StringComparison.OrdinalIgnoreCase) && digits.Length > 1)
        {
            digits = digits[1..];
            var dash = digits.IndexOf('-');
            if (dash > 0)
            {
                digits = digits[..dash];
            }
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
