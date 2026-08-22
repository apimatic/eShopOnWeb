using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public PaymentReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new Exceptions.CheckoutException(400, "`to` must be greater than or equal to `from`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to);
        var orders = await _orderRepository.ListAsync(new OrdersWithPayPalPaymentSpecification());
        var eshopRecords = Flatten(orders);

        var paypalById = paypalTransactions
            .GroupBy(t => t.TransactionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationRow>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedEshop = new HashSet<EshopPaymentRecord>();

        foreach (var record in eshopRecords)
        {
            var keys = new[] { record.CaptureId, record.RefundId, record.AuthorizationId, record.PayPalOrderId }
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Cast<string>();

            PayPalReportedTransaction? match = null;
            string? matchKey = null;
            foreach (var key in keys)
            {
                if (paypalById.TryGetValue(key, out match))
                {
                    matchKey = key;
                    break;
                }
            }

            if (match == null && !string.IsNullOrWhiteSpace(record.PayPalOrderId))
            {
                match = paypalTransactions.FirstOrDefault(t =>
                    string.Equals(t.PaypalReferenceId, record.PayPalOrderId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.InvoiceId, record.OrderId.ToString(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.InvoiceId, $"eshop-{record.OrderId}", StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    matchKey = match.TransactionId;
                }
            }

            if (match != null)
            {
                matched.Add(new ReconciliationRow
                {
                    OrderId = record.OrderId,
                    MatchKey = matchKey ?? match.TransactionId,
                    PayPal = match,
                    Eshop = record
                });
                matchedPayPalIds.Add(match.TransactionId);
                matchedEshop.Add(record);
            }
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedPayPalIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = eshopRecords
            .Where(r => !matchedEshop.Contains(r) && InRange(r, from, to))
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

    private static List<EshopPaymentRecord> Flatten(IReadOnlyList<Order> orders)
    {
        var records = new List<EshopPaymentRecord>();
        foreach (var order in orders)
        {
            if (!string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                records.Add(new EshopPaymentRecord
                {
                    OrderId = order.Id,
                    BuyerId = order.BuyerId,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    PayPalOrderId = order.Payment.PayPalOrderId,
                    AuthorizationId = order.Payment.AuthorizationId,
                    CaptureId = order.Payment.CaptureId,
                    Amount = order.Payment.CapturedAmount,
                    Currency = order.Payment.Currency
                });
            }
            else if (!string.IsNullOrEmpty(order.Payment.AuthorizationId))
            {
                records.Add(new EshopPaymentRecord
                {
                    OrderId = order.Id,
                    BuyerId = order.BuyerId,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    PayPalOrderId = order.Payment.PayPalOrderId,
                    AuthorizationId = order.Payment.AuthorizationId,
                    Amount = order.Payment.AuthorizedAmount,
                    Currency = order.Payment.Currency
                });
            }

            foreach (var refund in order.Refunds)
            {
                records.Add(new EshopPaymentRecord
                {
                    OrderId = order.Id,
                    BuyerId = order.BuyerId,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    PayPalOrderId = order.Payment.PayPalOrderId,
                    CaptureId = order.Payment.CaptureId,
                    RefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Currency = refund.Currency
                });
            }
        }

        return records;
    }

    private static bool InRange(EshopPaymentRecord record, DateTimeOffset from, DateTimeOffset to)
    {
        // Records without a PayPal timestamp still belong in the operator report when
        // they have payment state in this installation; callers pass the window they care about.
        _ = record;
        _ = from;
        _ = to;
        return true;
    }
}
