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
    private readonly IReadRepository<Order> _orders;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _payPalOptions;

    public ReconciliationService(
        IReadRepository<Order> orders,
        IPayPalGateway payPal,
        PayPalOptions payPalOptions)
    {
        _orders = orders;
        _payPal = payPal;
        _payPalOptions = payPalOptions;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.CheckoutException(400, "`to` must be on or after `from`.");
        }

        var currency = string.IsNullOrWhiteSpace(_payPalOptions.Currency)
            ? null
            : _payPalOptions.Currency.Trim().ToUpperInvariant();

        var paypalTxns = await _payPal.SearchTransactionsAsync(from, to, currency, cancellationToken);
        var orders = await _orders.ListAsync(new OrdersInDateRangeWithPaymentSpecification(from, to), cancellationToken);

        var rows = paypalTxns.Select(txn =>
        {
            var match = FindMatch(orders, txn);
            return new ReconciliationRow
            {
                TransactionId = txn.TransactionId,
                PaypalReferenceId = txn.PaypalReferenceId,
                PaypalReferenceIdType = txn.PaypalReferenceIdType,
                TransactionEventCode = txn.TransactionEventCode,
                TransactionInitiationDate = txn.TransactionInitiationDate,
                TransactionAmount = txn.TransactionAmount,
                CurrencyCode = txn.CurrencyCode,
                FeeAmount = txn.FeeAmount,
                TransactionStatus = txn.TransactionStatus,
                InvoiceId = txn.InvoiceId,
                CustomField = txn.CustomField,
                MatchedOrderId = match?.Id,
                MatchStatus = match is null ? "PayPalOnly" : "Matched"
            };
        }).ToList();

        var matchedIds = rows.Where(r => r.MatchedOrderId.HasValue).Select(r => r.MatchedOrderId!.Value).ToHashSet();
        var unmatchedOrders = orders
            .Where(o => !string.IsNullOrEmpty(o.PayPalOrderId) || !string.IsNullOrEmpty(o.PayPalCaptureId)
                || !string.IsNullOrEmpty(o.PayPalAuthorizationId))
            .Where(o => !matchedIds.Contains(o.Id) && !MatchedByIdentifiers(o, paypalTxns))
            .Select(o => new UnmatchedOrderRow
            {
                OrderId = o.Id,
                BuyerId = o.BuyerId,
                PaymentStatus = o.PaymentStatus.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                PayPalAuthorizationId = o.PayPalAuthorizationId,
                PayPalCaptureId = o.PayPalCaptureId,
                Total = o.Total(),
                OrderDate = o.OrderDate
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactions = rows,
            EshopOrdersWithoutPayPalMatch = unmatchedOrders
        };
    }

    private static Order? FindMatch(IReadOnlyList<Order> orders, PayPalTransactionRecord txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order, txn))
            {
                return order;
            }
        }

        return null;
    }

    private static bool MatchedByIdentifiers(Order order, IReadOnlyList<PayPalTransactionRecord> txns) =>
        txns.Any(t => Matches(order, t));

    private static bool Matches(Order order, PayPalTransactionRecord txn)
    {
        if (!string.IsNullOrEmpty(txn.CustomField) && txn.CustomField == order.Id.ToString())
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.InvoiceId) &&
            (txn.InvoiceId == $"ESHOP-{order.Id}" || txn.InvoiceId == order.Id.ToString()))
        {
            return true;
        }

        return IdentifiersOf(order).Contains(txn.TransactionId)
            || IdentifiersOf(order).Contains(txn.PaypalReferenceId);
    }

    private static HashSet<string> IdentifiersOf(Order order)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, order.PayPalOrderId);
        Add(ids, order.PayPalAuthorizationId);
        Add(ids, order.PayPalCaptureId);
        foreach (var refund in order.Refunds)
        {
            Add(ids, refund.PayPalRefundId);
        }

        return ids;
    }

    private static void Add(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            set.Add(value);
        }
    }
}
