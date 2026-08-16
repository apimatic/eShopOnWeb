using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(IPayPalPaymentGateway payPal, IReadRepository<Order> orderRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.");

        // PayPal's own record for the range (across all pages).
        var payPalTxns = await _payPal.ListTransactionsAsync(from, to, ct);

        // eShop's own record: every order that moved money.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct);

        // Index the PayPal-owned ids we already know about, so we can line transactions up to orders.
        var ordersByTransactionId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var ordersById = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var p = order.Payment!;
            // Match on the exact custom_id we stamped (globally unique), not the bare order id.
            AddKey(ordersById, p.PayPalCustomId, order);
            AddKey(ordersByTransactionId, p.AuthorizationId, order);
            AddKey(ordersByTransactionId, p.CaptureId, order);
            foreach (var r in p.Refunds) AddKey(ordersByTransactionId, r.PayPalRefundId, order);
        }

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in payPalTxns)
        {
            var order = ResolveOrder(txn, ordersByTransactionId, ordersById);
            if (order is not null)
            {
                matchedOrderIds.Add(order.Id);
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.Matched,
                    OrderId = order.Id,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalStatus = txn.Status,
                    EventCode = txn.EventCode,
                    PayPalAmount = txn.Amount,
                    EShopAmount = order.Payment!.CapturedAmount ?? order.Payment.Amount,
                    CurrencyCode = txn.CurrencyCode,
                    Date = txn.Date
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.InPayPalOnly,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalStatus = txn.Status,
                    EventCode = txn.EventCode,
                    PayPalAmount = txn.Amount,
                    CurrencyCode = txn.CurrencyCode,
                    Date = txn.Date
                });
            }
        }

        // eShop payments PayPal did not report within the range.
        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id)) continue;
            if (order.OrderDate < from || order.OrderDate > to) continue; // outside the requested window
            var p = order.Payment!;
            lines.Add(new ReconciliationLine
            {
                Match = ReconciliationMatch.InEShopOnly,
                OrderId = order.Id,
                PayPalTransactionId = p.CaptureId ?? p.AuthorizationId,
                PayPalStatus = p.Status.ToString(),
                EShopAmount = p.CapturedAmount ?? p.Amount,
                CurrencyCode = p.CurrencyCode,
                Date = order.OrderDate
            });
        }

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = payPalTxns.Count,
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            InPayPalOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.InPayPalOnly),
            InEShopOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.InEShopOnly),
            Lines = lines
        };

        _logger.LogInformation($"Reconciliation {from:o}..{to:o}: {report.PayPalTransactionCount} PayPal txns, " +
            $"{report.MatchedCount} matched, {report.InPayPalOnlyCount} PayPal-only, {report.InEShopOnlyCount} eShop-only.");
        return report;
    }

    private static Order? ResolveOrder(PayPalTransaction txn,
        IReadOnlyDictionary<string, Order> byTransactionId, IReadOnlyDictionary<string, Order> byId)
    {
        if (byTransactionId.TryGetValue(txn.TransactionId, out var order))
            return order;
        if (!string.IsNullOrEmpty(txn.CustomId) && byId.TryGetValue(txn.CustomId!, out order))
            return order;
        if (!string.IsNullOrEmpty(txn.InvoiceId) && byId.TryGetValue(txn.InvoiceId!, out order))
            return order;
        return null;
    }

    private static void AddKey(IDictionary<string, Order> map, string? key, Order order)
    {
        if (!string.IsNullOrEmpty(key)) map[key!] = order;
    }
}
