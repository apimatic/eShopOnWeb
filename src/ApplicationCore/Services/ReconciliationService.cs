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
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpec(), cancellationToken);

        var eshopEntries = orders
            .SelectMany(ToLedgerEntries)
            .Where(e => e.At >= from && e.At <= to)
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPayPalOnly>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedEshopKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var match = FindMatch(txn, orders, eshopEntries);
            if (match is not null)
            {
                matched.Add(match);
                matchedPayPalIds.Add(txn.TransactionId);
                if (match.EshopPaymentId is not null)
                {
                    matchedEshopKeys.Add(match.EshopPaymentId);
                }
            }
            else
            {
                paypalOnly.Add(new ReconciliationPayPalOnly
                {
                    PayPalTransactionId = txn.TransactionId,
                    Status = txn.Status,
                    Amount = txn.Amount,
                    Currency = txn.Currency,
                    CustomField = txn.CustomField,
                    InvoiceId = txn.InvoiceId
                });
            }
        }

        var eshopOnly = eshopEntries
            .Where(e => !matchedEshopKeys.Contains(e.PayPalId) &&
                        !matchedPayPalIds.Contains(e.PayPalId))
            .Select(e => new ReconciliationEshopOnly
            {
                OrderId = e.OrderId,
                Kind = e.Kind,
                PayPalId = e.PayPalId,
                Status = e.Status
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = paypalTransactions.Count,
            EshopPaymentCount = eshopEntries.Count,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static ReconciliationMatch? FindMatch(
        PayPalReportedTransaction txn,
        IReadOnlyList<Order> orders,
        IReadOnlyList<EshopLedgerEntry> entries)
    {
        var byId = entries.FirstOrDefault(e =>
            string.Equals(e.PayPalId, txn.TransactionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.PayPalId, txn.ReferenceId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return new ReconciliationMatch
            {
                OrderId = byId.OrderId,
                PayPalTransactionId = txn.TransactionId,
                EshopPaymentId = byId.PayPalId,
                MatchReason = "paypal_id"
            };
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
            int.TryParse(txn.CustomField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderIdFromCustom))
        {
            var order = orders.FirstOrDefault(o => o.Id == orderIdFromCustom);
            if (order is not null)
            {
                return new ReconciliationMatch
                {
                    OrderId = order.Id,
                    PayPalTransactionId = txn.TransactionId,
                    EshopPaymentId = order.Payment?.CaptureId ?? order.Payment?.AuthorizationId,
                    MatchReason = "custom_id"
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(txn.InvoiceId) &&
            txn.InvoiceId.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = txn.InvoiceId["ESHOP-".Length..];
            var orderToken = suffix.Split('-', 2)[0];
            if (int.TryParse(orderToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderIdFromInvoice))
            {
                var order = orders.FirstOrDefault(o => o.Id == orderIdFromInvoice);
                if (order is not null)
                {
                    return new ReconciliationMatch
                    {
                        OrderId = order.Id,
                        PayPalTransactionId = txn.TransactionId,
                        EshopPaymentId = order.Payment?.CaptureId ?? order.Payment?.AuthorizationId,
                        MatchReason = "invoice_id"
                    };
                }
            }
        }

        return null;
    }

    private static IEnumerable<EshopLedgerEntry> ToLedgerEntries(Order order)
    {
        if (order.Payment is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            yield return new EshopLedgerEntry(
                order.Id,
                "authorization",
                order.Payment.AuthorizationId,
                order.Payment.AuthorizationStatus,
                order.Payment.AuthorizedAt ?? order.OrderDate);
        }

        if (!string.IsNullOrWhiteSpace(order.Payment.CaptureId))
        {
            yield return new EshopLedgerEntry(
                order.Id,
                "capture",
                order.Payment.CaptureId,
                order.Payment.CaptureStatus,
                order.Payment.CapturedAt ?? order.Payment.AuthorizedAt ?? order.OrderDate);
        }

        foreach (var refund in order.Refunds)
        {
            yield return new EshopLedgerEntry(
                order.Id,
                "refund",
                refund.PayPalRefundId,
                refund.Status,
                refund.CreatedAt);
        }
    }

    private sealed record EshopLedgerEntry(int OrderId, string Kind, string PayPalId, string? Status, DateTimeOffset At);
}
