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
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orders)
    {
        _payPal = payPal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentRequestException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListAllTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orders.ListAsync(new OrdersWithPayPalReferencesSpec(), cancellationToken);

        var eshopPayments = FlattenEshopPayments(eshopOrders)
            .Where(p => InRange(p.OccurredAt, from, to) || paypalTransactions.Any(t => Matches(t, p)))
            .ToList();

        var matchedPaypal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedEshop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ReconciliationRow>();

        foreach (var txn in paypalTransactions)
        {
            var match = eshopPayments.FirstOrDefault(p => Matches(txn, p));
            if (match != null)
            {
                matchedPaypal.Add(txn.TransactionId);
                matchedEshop.Add(match.Key);
                rows.Add(new ReconciliationRow(
                    "matched",
                    match.OrderId,
                    txn.TransactionId,
                    match.PayPalId,
                    txn.InvoiceId ?? match.InvoiceId,
                    txn.Amount,
                    match.Amount,
                    txn.Currency ?? match.Currency,
                    txn.Status,
                    match.Status,
                    txn.InitiationDate));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    "paypal_only",
                    TryParseOrderId(txn.InvoiceId, txn.CustomField),
                    txn.TransactionId,
                    null,
                    txn.InvoiceId,
                    txn.Amount,
                    null,
                    txn.Currency,
                    txn.Status,
                    null,
                    txn.InitiationDate));
            }
        }

        foreach (var payment in eshopPayments.Where(p => !matchedEshop.Contains(p.Key)))
        {
            rows.Add(new ReconciliationRow(
                "eshop_only",
                payment.OrderId,
                null,
                payment.PayPalId,
                payment.InvoiceId,
                null,
                payment.Amount,
                payment.Currency,
                null,
                payment.Status,
                payment.OccurredAt));
        }

        return new ReconciliationReport(
            from,
            to,
            paypalTransactions.Count,
            eshopPayments.Count,
            matchedPaypal.Count,
            rows.Count(r => r.MatchStatus == "paypal_only"),
            rows.Count(r => r.MatchStatus == "eshop_only"),
            rows);
    }

    private static bool Matches(PayPalReportedTransaction txn, EshopPaymentRef payment)
    {
        if (!string.IsNullOrEmpty(txn.TransactionId) && IdsEqual(txn.TransactionId, payment.PayPalId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.ReferenceId) && IdsEqual(txn.ReferenceId, payment.PayPalId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.InvoiceId) &&
            string.Equals(txn.InvoiceId, payment.InvoiceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.CustomField) &&
            (string.Equals(txn.CustomField, payment.OrderId.ToString(), StringComparison.OrdinalIgnoreCase)
             || string.Equals(txn.CustomField, payment.InvoiceId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool IdsEqual(string left, string? right) =>
        !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value.HasValue && value.Value >= from && value.Value <= to;

    private static int? TryParseOrderId(string? invoiceId, string? customField)
    {
        if (int.TryParse(customField, out var fromCustom))
        {
            return fromCustom;
        }

        if (!string.IsNullOrEmpty(invoiceId) && invoiceId.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase))
        {
            var token = invoiceId["ESHOP-".Length..].Split('-')[0];
            if (int.TryParse(token, out var fromInvoice))
            {
                return fromInvoice;
            }
        }

        return null;
    }

    private static List<EshopPaymentRef> FlattenEshopPayments(IEnumerable<Order> orders)
    {
        var list = new List<EshopPaymentRef>();
        foreach (var order in orders)
        {
            var invoiceId = order.Payment.MerchantInvoiceId;
            if (string.IsNullOrEmpty(invoiceId))
            {
                invoiceId = $"ESHOP-{order.Id}";
            }
            if (!string.IsNullOrEmpty(order.Payment.AuthorizationId))
            {
                list.Add(new EshopPaymentRef(
                    $"auth:{order.Payment.AuthorizationId}",
                    order.Id,
                    order.Payment.AuthorizationId,
                    invoiceId,
                    order.Payment.AuthorizedAmount,
                    order.Payment.Currency,
                    order.Payment.AuthorizationStatus ?? order.Status.ToString(),
                    order.Payment.AuthorizedAt ?? order.OrderDate));
            }

            if (!string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                list.Add(new EshopPaymentRef(
                    $"cap:{order.Payment.CaptureId}",
                    order.Id,
                    order.Payment.CaptureId,
                    invoiceId,
                    order.Payment.CapturedAmount,
                    order.Payment.Currency,
                    order.Payment.CaptureStatus ?? order.Status.ToString(),
                    order.Payment.CapturedAt ?? order.OrderDate));
            }

            foreach (var refund in order.Refunds)
            {
                list.Add(new EshopPaymentRef(
                    $"ref:{refund.PayPalRefundId}",
                    order.Id,
                    refund.PayPalRefundId,
                    invoiceId,
                    refund.Amount,
                    refund.Currency,
                    refund.Status,
                    refund.CreatedAt));
            }
        }

        return list;
    }

    private sealed record EshopPaymentRef(
        string Key,
        int OrderId,
        string PayPalId,
        string InvoiceId,
        decimal? Amount,
        string? Currency,
        string Status,
        DateTimeOffset? OccurredAt);
}
