using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction record for a date range up against eShop's orders. Anything PayPal
/// knows about that eShop doesn't — or the reverse — is surfaced. Coverage is whole-range: the PayPal
/// client pages through and chunks the window, and we compare against every eShop order that carries a
/// payment.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPalClient;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPalClient, IReadRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.", nameof(to));

        var payPalTransactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // Index every PayPal id and the exact custom reference eShop sent, mapping back to the owning
        // order. Matching is case-sensitive (Ordinal): the custom reference carries a random token and
        // PayPal ids are case-significant, so this avoids colliding with unrelated transactions that a
        // prior run left in a shared sandbox account.
        var idToOrder = new Dictionary<string, Order>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            Index(idToOrder, payment.PayPalCustomId, order);
            Index(idToOrder, payment.PayPalOrderId, order);
            Index(idToOrder, payment.AuthorizationId, order);
            Index(idToOrder, payment.CaptureId, order);
            foreach (var refund in payment.Refunds)
                Index(idToOrder, refund.PayPalRefundId, order);
        }

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        // PayPal's side: every transaction it reports, matched to an eShop order where possible.
        foreach (var txn in payPalTransactions)
        {
            var matchedOrder = ResolveOrder(idToOrder, txn);
            if (matchedOrder is not null)
            {
                matchedOrderIds.Add(matchedOrder.Id);
                lines.Add(new ReconciliationLine(
                    ReconciliationMatchState.Matched, txn.TransactionId, txn.Status, txn.EventCode,
                    txn.Amount, txn.Currency, txn.Date, matchedOrder.Id,
                    matchedOrder.Payment!.CaptureId ?? matchedOrder.Payment.AuthorizationId));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationMatchState.InPayPalNotInEShop, txn.TransactionId, txn.Status, txn.EventCode,
                    txn.Amount, txn.Currency, txn.Date, OrderId: null, EShopPaymentReference: null));
            }
        }

        // eShop's side: captured payments dated in the range that no PayPal transaction lined up with.
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (!payment.IsCaptured) continue;
            if (matchedOrderIds.Contains(order.Id)) continue;
            if (order.OrderDate < from || order.OrderDate > to) continue;

            lines.Add(new ReconciliationLine(
                ReconciliationMatchState.InEShopNotInPayPal, PayPalTransactionId: null, PayPalStatus: null,
                EventCode: null, payment.CapturedAmount, payment.Currency, order.OrderDate, order.Id,
                payment.CaptureId));
        }

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: payPalTransactions.Count,
            MatchedCount: lines.Count(l => l.MatchState == ReconciliationMatchState.Matched),
            InPayPalNotInEShopCount: lines.Count(l => l.MatchState == ReconciliationMatchState.InPayPalNotInEShop),
            InEShopNotInPayPalCount: lines.Count(l => l.MatchState == ReconciliationMatchState.InEShopNotInPayPal),
            Lines: lines);
    }

    private static Order? ResolveOrder(IReadOnlyDictionary<string, Order> idToOrder, PayPalTransaction txn)
    {
        if (idToOrder.TryGetValue(txn.TransactionId, out var byTxn))
            return byTxn;
        if (txn.CustomField is not null && idToOrder.TryGetValue(txn.CustomField, out var byCustom))
            return byCustom;
        if (txn.InvoiceId is not null && idToOrder.TryGetValue(txn.InvoiceId, out var byInvoice))
            return byInvoice;
        if (txn.PayPalReferenceId is not null && idToOrder.TryGetValue(txn.PayPalReferenceId, out var byRef))
            return byRef;
        return null;
    }

    private static void Index(IDictionary<string, Order> map, string? key, Order order)
    {
        if (!string.IsNullOrEmpty(key))
            map[key] = order;
    }
}
