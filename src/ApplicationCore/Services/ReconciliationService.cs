using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the reconciliation report: it pulls PayPal's own record of transactions for the range and lines
/// them up against eShop orders, surfacing anything one side knows about and the other doesn't.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalPaymentGateway payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // eShop side: only orders that have actually moved money (captured) are expected to appear in PayPal's
        // reporting. Index them by every identifier PayPal might reference them by.
        var eShopOrders = orders.Where(o => o.Payment is { } p && (p.HasCapture || p.HasAuthorization)).ToList();
        var ordersByReference = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in eShopOrders)
        {
            foreach (var key in ReferenceKeys(order))
            {
                ordersByReference[key] = order;
            }
        }

        var matched = new List<ReconciliationEntry>();
        var missingInEShop = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = ResolveOrder(txn, ordersByReference);
            if (order is not null)
            {
                matchedOrderIds.Add(order.Id);
                matched.Add(new ReconciliationEntry(
                    order.Id.ToString(),
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.Amount,
                    txn.CurrencyCode,
                    txn.Status,
                    txn.Date,
                    $"Matched PayPal transaction {txn.TransactionId} to eShop order {order.Id}."));
            }
            else
            {
                missingInEShop.Add(new ReconciliationEntry(
                    null,
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.Amount,
                    txn.CurrencyCode,
                    txn.Status,
                    txn.Date,
                    "PayPal has this transaction but eShop has no matching order."));
            }
        }

        // eShop orders that captured/held money in the range but that no PayPal transaction referenced.
        var missingInPayPal = eShopOrders
            .Where(o => !matchedOrderIds.Contains(o.Id) && WithinRange(o, from, to))
            .Select(o => new ReconciliationEntry(
                o.Id.ToString(),
                o.Payment!.CaptureId ?? o.Payment!.AuthorizationId,
                o.Payment!.InvoiceReference ?? $"ESHOP-{o.Id}",
                o.Payment!.CapturedGross ?? o.Payment!.Amount,
                o.Payment!.Currency,
                o.Status.ToString(),
                o.OrderDate,
                "eShop recorded this payment but no PayPal transaction was found in the range (PayPal reporting can lag by up to a few hours)."))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, eShopOrders.Count, matched, missingInEShop, missingInPayPal);
    }

    private static IEnumerable<string> ReferenceKeys(Order order)
    {
        var p = order.Payment!;
        yield return $"ESHOP-{order.Id}";
        if (!string.IsNullOrEmpty(p.InvoiceReference)) yield return p.InvoiceReference!;
        if (!string.IsNullOrEmpty(p.PayPalOrderId)) yield return p.PayPalOrderId!;
        if (!string.IsNullOrEmpty(p.AuthorizationId)) yield return p.AuthorizationId!;
        if (!string.IsNullOrEmpty(p.CaptureId)) yield return p.CaptureId!;
        foreach (var refund in p.Refunds)
        {
            if (!string.IsNullOrEmpty(refund.RefundId)) yield return refund.RefundId;
        }
    }

    private static Order? ResolveOrder(PayPalTransaction txn, IReadOnlyDictionary<string, Order> ordersByReference)
    {
        if (!string.IsNullOrEmpty(txn.InvoiceId) && ordersByReference.TryGetValue(txn.InvoiceId!, out var byInvoice))
        {
            return byInvoice;
        }
        if (!string.IsNullOrEmpty(txn.CustomField) && ordersByReference.TryGetValue(txn.CustomField!, out var byCustom))
        {
            return byCustom;
        }
        if (!string.IsNullOrEmpty(txn.TransactionId) && ordersByReference.TryGetValue(txn.TransactionId, out var byTxn))
        {
            return byTxn;
        }
        return null;
    }

    private static bool WithinRange(Order order, DateTimeOffset from, DateTimeOffset to)
        => order.OrderDate >= from && order.OrderDate <= to;
}
