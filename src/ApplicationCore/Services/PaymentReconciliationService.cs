using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentsClient _paypal;

    public PaymentReconciliationService(
        IRepository<Order> orderRepository,
        IPayPalPaymentsClient paypal)
    {
        _orderRepository = orderRepository;
        _paypal = paypal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.");
        }

        IReadOnlyList<PayPalReportedTransaction> paypalTransactions;
        try
        {
            paypalTransactions = await _paypal.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PaymentException ex) when (
            ex.StatusCode == 404
            || ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase))
        {
            // Transaction Search lags live activity and may have no page for a recent window.
            paypalTransactions = Array.Empty<PayPalReportedTransaction>();
        }
        var orders = (await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken)).ToList();

        var matched = new List<MatchedReconciliationRow>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in paypalTransactions)
        {
            var order = orders.FirstOrDefault(o => Matches(o, transaction));
            if (order == null)
            {
                paypalOnly.Add(transaction);
                continue;
            }

            matched.Add(new MatchedReconciliationRow
            {
                PaypalTransaction = transaction,
                Order = order
            });
            matchedOrderIds.Add(order.Id);
        }

        // A PayPal row might also match an order outside the local date filter (clock skew).
        if (paypalOnly.Count > 0)
        {
            var unmatchedIds = paypalOnly
                .Select(t => TryParseOrderId(t))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();

            if (unmatchedIds.Length > 0)
            {
                foreach (var orderId in unmatchedIds)
                {
                    var extra = await _orderRepository.FirstOrDefaultAsync(
                        new OrderByIdWithPaymentSpec(orderId), cancellationToken);
                    if (extra != null)
                    {
                        orders.Add(extra);
                    }
                }

                var stillUnmatched = new List<PayPalReportedTransaction>();
                foreach (var transaction in paypalOnly)
                {
                    var order = orders.FirstOrDefault(o => Matches(o, transaction));
                    if (order == null)
                    {
                        stillUnmatched.Add(transaction);
                        continue;
                    }

                    matched.Add(new MatchedReconciliationRow
                    {
                        PaypalTransaction = transaction,
                        Order = order
                    });
                    matchedOrderIds.Add(order.Id);
                }

                paypalOnly = stillUnmatched;
            }
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaymentFootprint(o))
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PaypalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static bool HasPaymentFootprint(Order order)
    {
        return !string.IsNullOrEmpty(order.PaypalOrderId)
               || !string.IsNullOrEmpty(order.PaypalAuthorizationId)
               || !string.IsNullOrEmpty(order.PaypalCaptureId);
    }

    private static bool Matches(Order order, PayPalReportedTransaction transaction)
    {
        if (!string.IsNullOrEmpty(transaction.CustomField)
            && string.Equals(transaction.CustomField, order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(transaction.InvoiceId)
            && (string.Equals(transaction.InvoiceId, order.InvoiceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(transaction.InvoiceId, $"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)
                || transaction.InvoiceId.StartsWith($"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (IdsEqual(transaction.TransactionId, order.PaypalCaptureId)
            || IdsEqual(transaction.TransactionId, order.PaypalAuthorizationId)
            || IdsEqual(transaction.TransactionId, order.PaypalOrderId)
            || order.Refunds.Any(r => IdsEqual(transaction.TransactionId, r.PaypalRefundId)))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(transaction.ReferenceId)
            && (IdsEqual(transaction.ReferenceId, order.PaypalCaptureId)
                || IdsEqual(transaction.ReferenceId, order.PaypalAuthorizationId)
                || IdsEqual(transaction.ReferenceId, order.PaypalOrderId)))
        {
            return true;
        }

        return false;
    }

    private static bool IdsEqual(string? left, string? right)
    {
        return !string.IsNullOrEmpty(left)
               && !string.IsNullOrEmpty(right)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryParseOrderId(PayPalReportedTransaction transaction)
    {
        if (int.TryParse(transaction.CustomField, out var fromCustom))
        {
            return fromCustom;
        }

        var invoice = transaction.InvoiceId;
        if (string.IsNullOrEmpty(invoice))
        {
            return null;
        }

        if (invoice.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase))
        {
            var rest = invoice["ESHOP-".Length..];
            var orderPart = rest.Split('-', 2)[0];
            if (int.TryParse(orderPart, out var fromInvoice))
            {
                return fromInvoice;
            }
        }

        return null;
    }
}
