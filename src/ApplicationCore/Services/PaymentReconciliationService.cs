using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IReadRepository<Order> _orders;
    private readonly IPayPalGateway _payPal;

    public PaymentReconciliationService(IReadRepository<Order> orders, IPayPalGateway payPal)
    {
        _orders = orders;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to);
        var orders = (await _orders.ListAsync(new OrdersWithPaymentByDateRangeSpec(from, to)))
            .Where(o => o.Payment is not null)
            .ToList();

        var matchedPaypal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ReconciliationRow>();

        foreach (var order in orders)
        {
            var payment = order.Payment!;
            var ids = CollectPaypalIds(payment);
            var match = paypalTransactions.FirstOrDefault(t => TransactionMatches(t, order, ids));
            if (match is null)
            {
                rows.Add(new ReconciliationRow(
                    "eshop-only",
                    null,
                    order.Id,
                    "eShop has this payment; PayPal's report does not",
                    null,
                    order.Status.ToString(),
                    null,
                    order.Total(),
                    payment.Currency,
                    order.OrderDate));
                continue;
            }

            matchedPaypal.Add(match.TransactionId);
            rows.Add(new ReconciliationRow(
                "matched",
                match.TransactionId,
                order.Id,
                "PayPal and eShop both have this payment",
                match.Status,
                order.Status.ToString(),
                match.Amount,
                order.Total(),
                match.Currency ?? payment.Currency,
                match.InitiationDate));
        }

        foreach (var txn in paypalTransactions.Where(t => !matchedPaypal.Contains(t.TransactionId)))
        {
            rows.Add(new ReconciliationRow(
                "paypal-only",
                txn.TransactionId,
                null,
                "PayPal has this transaction; eShop has no matching order",
                txn.Status,
                null,
                txn.Amount,
                null,
                txn.Currency,
                txn.InitiationDate));
        }

        return new ReconciliationReport(from, to, paypalTransactions.Count, orders.Count, rows);
    }

    private static HashSet<string> CollectPaypalIds(OrderPayment payment)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, payment.PaypalOrderId);
        Add(ids, payment.PaypalAuthorizationId);
        Add(ids, payment.PaypalCaptureId);
        foreach (var refund in payment.Refunds)
        {
            Add(ids, refund.PaypalRefundId);
        }

        return ids;

        static void Add(HashSet<string> set, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value);
            }
        }
    }

    private static bool TransactionMatches(PayPalTransactionRecord txn, Order order, HashSet<string> paypalIds)
    {
        if (!string.IsNullOrWhiteSpace(txn.TransactionId) && paypalIds.Contains(txn.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.PaypalReferenceId) && paypalIds.Contains(txn.PaypalReferenceId))
        {
            return true;
        }

        var orderKey = order.Id.ToString(CultureInfo.InvariantCulture);
        var invoice = $"eShop-{order.Id}";
        return string.Equals(txn.InvoiceId, invoice, StringComparison.OrdinalIgnoreCase)
               || string.Equals(txn.InvoiceId, orderKey, StringComparison.OrdinalIgnoreCase)
               || string.Equals(txn.CustomField, orderKey, StringComparison.OrdinalIgnoreCase)
               || string.Equals(txn.CustomField, invoice, StringComparison.OrdinalIgnoreCase);
    }
}
