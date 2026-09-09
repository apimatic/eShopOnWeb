using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly PaymentReferenceFactory _references;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Order> orderRepository,
        PaymentReferenceFactory references)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
        _references = references;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (from > to)
            throw new PaymentOperationException("'from' must be earlier than or equal to 'to'.", 400);

        // PayPal's own record across the whole range (client chunks the span and pages to completion).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // eShop's record: orders placed in the range that have a PayPal order attached.
        var orders = await _orderRepository.ListAsync(new OrdersWithPayPalActivitySpec(from, to), ct);
        var ordersById = orders.ToDictionary(o => o.Id.ToString(CultureInfo.InvariantCulture));

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<string>();

        foreach (var txn in transactions)
        {
            var orderId = ResolveOrderId(txn);
            if (orderId is not null && ordersById.TryGetValue(orderId, out var order))
            {
                matchedOrderIds.Add(orderId);
                lines.Add(new ReconciliationLine(
                    "Matched", txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency,
                    order.Id, order.Total(), order.Payment.Status.ToString(),
                    txn.InvoiceId, txn.CustomField, txn.Date));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    "PayPalOnly", txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency,
                    null, null, null, txn.InvoiceId, txn.CustomField, txn.Date));
            }
        }

        // Orders eShop recorded that PayPal's report doesn't show (e.g. reporting lag).
        foreach (var order in orders)
        {
            var id = order.Id.ToString(CultureInfo.InvariantCulture);
            if (matchedOrderIds.Contains(id)) continue;
            lines.Add(new ReconciliationLine(
                "EShopOnly", null, null, null, null, order.Payment.Currency,
                order.Id, order.Total(), order.Payment.Status.ToString(),
                order.Payment.PayPalOrderId, id, order.OrderDate));
        }

        var matched = lines.Count(l => l.MatchState == "Matched");
        var payPalOnly = lines.Count(l => l.MatchState == "PayPalOnly");
        var eShopOnly = lines.Count(l => l.MatchState == "EShopOnly");

        return new ReconciliationReport(from, to, transactions.Count, orders.Count,
            matched, payPalOnly, eShopOnly, lines);
    }

    /// <summary>
    /// eShop stamps each PayPal purchase unit with custom_id = "{runId}-{orderId}", so a transaction
    /// is lined up to an order only when it carries THIS run's reference. Transactions from other runs
    /// or unrelated account activity correctly fall through as "PayPal only".
    /// </summary>
    private string? ResolveOrderId(PayPalTransactionRecord txn)
    {
        var byCustom = _references.TryGetOrderId(txn.CustomField);
        return byCustom?.ToString(CultureInfo.InvariantCulture);
    }
}
