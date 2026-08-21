using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListAllTransactionsAsync(from, to);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpec());
        var eshopEntries = FlattenEshopPayments(orders, from, to);

        var matchedPaypal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedEshop = new HashSet<int>();
        var rows = new List<ReconciliationRow>();

        foreach (var txn in paypalTransactions)
        {
            var match = eshopEntries.FirstOrDefault(e => Matches(e, txn));
            if (match != null)
            {
                matchedPaypal.Add(txn.TransactionId);
                matchedEshop.Add(match.Key);
                rows.Add(new ReconciliationRow(
                    match.OrderId.ToString(),
                    txn.TransactionId,
                    "Matched",
                    match.State,
                    txn.Status,
                    txn.Amount ?? match.Amount,
                    txn.Currency ?? match.Currency,
                    txn.InitiationDate ?? match.OccurredAt));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    null,
                    txn.TransactionId,
                    "PayPalOnly",
                    null,
                    txn.Status,
                    txn.Amount,
                    txn.Currency,
                    txn.InitiationDate));
            }
        }

        foreach (var entry in eshopEntries.Where(e => !matchedEshop.Contains(e.Key)))
        {
            rows.Add(new ReconciliationRow(
                entry.OrderId.ToString(),
                entry.PayPalId,
                "EshopOnly",
                entry.State,
                null,
                entry.Amount,
                entry.Currency,
                entry.OccurredAt));
        }

        var matchedCount = rows.Count(r => r.MatchStatus == "Matched");
        var paypalOnly = rows.Count(r => r.MatchStatus == "PayPalOnly");
        var eshopOnly = rows.Count(r => r.MatchStatus == "EshopOnly");

        return new ReconciliationReport(
            from,
            to,
            rows,
            paypalTransactions.Count,
            eshopEntries.Count,
            matchedCount,
            paypalOnly,
            eshopOnly);
    }

    private static List<EshopPaymentEntry> FlattenEshopPayments(IEnumerable<Order> orders, DateTimeOffset from, DateTimeOffset to)
    {
        var entries = new List<EshopPaymentEntry>();
        var key = 0;

        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment == null)
            {
                continue;
            }

            if (payment.AuthorizationCreatedAt is DateTimeOffset authorizedAt && InRange(authorizedAt, from, to))
            {
                entries.Add(new EshopPaymentEntry(
                    key++,
                    order.Id,
                    payment.AuthorizationId ?? payment.PayPalOrderId,
                    "Authorized",
                    order.Total().ToString("0.00"),
                    payment.Currency,
                    authorizedAt,
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    null,
                    Money.InvoicePrefix(order.Id),
                    Money.CustomId(order.Id)));
            }

            if (payment.CapturedAt is DateTimeOffset capturedAt && InRange(capturedAt, from, to))
            {
                entries.Add(new EshopPaymentEntry(
                    key++,
                    order.Id,
                    payment.CaptureId,
                    "Captured",
                    payment.CapturedAmount.ToString("0.00"),
                    payment.Currency,
                    capturedAt,
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    null,
                    Money.InvoicePrefix(order.Id),
                    Money.CustomId(order.Id)));
            }

            foreach (var refund in order.Refunds)
            {
                if (!InRange(refund.CreatedAt, from, to))
                {
                    continue;
                }

                entries.Add(new EshopPaymentEntry(
                    key++,
                    order.Id,
                    refund.PayPalRefundId,
                    "Refunded",
                    refund.Amount.ToString("0.00"),
                    payment.Currency,
                    refund.CreatedAt,
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    refund.PayPalRefundId,
                    Money.InvoicePrefix(order.Id),
                    Money.CustomId(order.Id)));
            }
        }

        return entries;
    }

    private static bool Matches(EshopPaymentEntry entry, PayPalReportedTransaction txn)
    {
        var candidates = new[]
        {
            txn.TransactionId,
            txn.ReferenceId,
            txn.InvoiceId,
            txn.CustomField
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (IdsEqual(candidate, entry.PayPalId) ||
                IdsEqual(candidate, entry.PayPalOrderId) ||
                IdsEqual(candidate, entry.AuthorizationId) ||
                IdsEqual(candidate, entry.CaptureId) ||
                IdsEqual(candidate, entry.RefundId) ||
                IdsEqual(candidate, entry.InvoiceId) ||
                IdsEqual(candidate, entry.CustomId) ||
                candidate.Contains(entry.InvoiceId, StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains(entry.CustomId, StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains(entry.OrderId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IdsEqual(string left, string? right) =>
        !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private sealed record EshopPaymentEntry(
        int Key,
        int OrderId,
        string? PayPalId,
        string State,
        string Amount,
        string? Currency,
        DateTimeOffset OccurredAt,
        string? PayPalOrderId,
        string? AuthorizationId,
        string? CaptureId,
        string? RefundId,
        string InvoiceId,
        string CustomId);
}
