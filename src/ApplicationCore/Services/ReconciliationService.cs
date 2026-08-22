using System;
using System.Collections.Generic;
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
            throw new Exceptions.PaymentException("'to' must be greater than or equal to 'from'.", 400);
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersForReconciliationSpec(from, to), cancellationToken);

        var unmatchedOrders = orders.ToList();
        var rows = new List<ReconciliationRow>();
        var matched = 0;
        var paypalOnly = 0;

        foreach (var tx in paypalTransactions)
        {
            var order = FindMatchingOrder(unmatchedOrders, orders, tx);
            if (order != null)
            {
                unmatchedOrders.Remove(order);
                matched++;
                rows.Add(new ReconciliationRow(
                    "matched",
                    order.Id,
                    order.PaymentStatus.ToString(),
                    tx.TransactionId,
                    tx.ReferenceId,
                    tx.InvoiceId ?? order.InvoiceId,
                    tx.Amount,
                    tx.Currency,
                    tx.TransactionDate));
            }
            else
            {
                paypalOnly++;
                rows.Add(new ReconciliationRow(
                    "paypal_only",
                    TryParseOrderId(tx.InvoiceId) ?? TryParseOrderId(tx.CustomField),
                    null,
                    tx.TransactionId,
                    tx.ReferenceId,
                    tx.InvoiceId,
                    tx.Amount,
                    tx.Currency,
                    tx.TransactionDate));
            }
        }

        foreach (var order in unmatchedOrders)
        {
            rows.Add(new ReconciliationRow(
                "eshop_only",
                order.Id,
                order.PaymentStatus.ToString(),
                order.PayPalCaptureId ?? order.PayPalAuthorizationId ?? order.PayPalOrderId,
                order.PayPalAuthorizationId,
                order.InvoiceId,
                MoneyFormat.ToPayPalValue(order.CapturedAmount ?? order.Total()),
                order.Currency,
                order.CapturedAt ?? order.AuthorizedAt ?? order.OrderDate));
        }

        return new ReconciliationReport(
            from,
            to,
            rows,
            paypalTransactions.Count,
            orders.Count,
            matched,
            paypalOnly,
            unmatchedOrders.Count);
    }

    private static Order? FindMatchingOrder(
        List<Order> unmatched,
        IReadOnlyList<Order> all,
        PayPalReportedTransaction tx)
    {
        var pool = unmatched.Concat(all).Distinct().ToList();
        foreach (var order in pool)
        {
            if (Matches(order, tx))
            {
                return unmatched.Contains(order) ? order : unmatched.FirstOrDefault(o => o.Id == order.Id) ?? order;
            }
        }

        return null;
    }

    private static bool Matches(Order order, PayPalReportedTransaction tx)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, order.PayPalOrderId);
        Add(ids, order.PayPalAuthorizationId);
        Add(ids, order.PayPalCaptureId);
        Add(ids, order.InvoiceId);
        foreach (var refund in order.Refunds)
        {
            Add(ids, refund.PayPalRefundId);
        }

        if (!string.IsNullOrEmpty(tx.TransactionId) && ids.Contains(tx.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.ReferenceId) && ids.Contains(tx.ReferenceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.InvoiceId) && ids.Contains(tx.InvoiceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(tx.CustomField) && ids.Contains(tx.CustomField))
        {
            return true;
        }

        var parsed = TryParseOrderId(tx.InvoiceId) ?? TryParseOrderId(tx.CustomField);
        return parsed != null && parsed == order.Id;
    }

    private static void Add(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static int? TryParseOrderId(string? invoiceOrCustom)
    {
        if (string.IsNullOrWhiteSpace(invoiceOrCustom))
        {
            return null;
        }

        const string prefix = "ESHOP-";
        if (!invoiceOrCustom.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = invoiceOrCustom[prefix.Length..];
        var dash = rest.IndexOf('-', StringComparison.Ordinal);
        var idPart = dash >= 0 ? rest[..dash] : rest;
        return int.TryParse(idPart, out var id) ? id : null;
    }
}
