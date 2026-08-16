using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalPaymentGateway gateway,
        IReadRepository<Order> orderRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' date must be on or after the 'from' date.");
        }

        // PayPal's own record for the range (windowed + fully paged by the gateway).
        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);

        // Every PayPal id eShop knows about (authorization, capture, refund ids), mapped back to its order.
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var eShopIdToOrder = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddId(eShopIdToOrder, order.AuthorizationId, order);
            AddId(eShopIdToOrder, order.CaptureId, order);
            foreach (var refund in order.Refunds)
            {
                AddId(eShopIdToOrder, refund.RefundId, order);
            }
        }

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();
        var matchedCount = 0;
        var payPalOnlyCount = 0;

        // Direction 1: every PayPal transaction, matched to an eShop order where possible.
        foreach (var tx in transactions)
        {
            var matched = eShopIdToOrder.TryGetValue(tx.TransactionId, out var order);
            if (matched)
            {
                matchedCount++;
                matchedOrderIds.Add(order!.Id);
            }
            else
            {
                payPalOnlyCount++;
            }

            rows.Add(new ReconciliationRow(
                PayPalTransactionId: tx.TransactionId,
                PayPalStatus: tx.Status,
                PayPalAmount: tx.Amount,
                PayPalFee: tx.Fee,
                PayPalDate: tx.Date,
                OrderId: matched ? order!.Id : null,
                OrderPaymentStatus: matched ? order!.PaymentStatus.ToString() : null,
                OrderTotal: matched ? order!.Total() : null,
                MatchState: matched ? "Matched" : "InPayPalNotInEShop"));
        }

        // Direction 2: eShop orders with PayPal activity that PayPal's report doesn't (yet) show.
        var eShopOnlyCount = 0;
        foreach (var order in orders)
        {
            if (order.PayPalOrderId is null || matchedOrderIds.Contains(order.Id))
            {
                continue;
            }

            eShopOnlyCount++;
            rows.Add(new ReconciliationRow(
                PayPalTransactionId: null,
                PayPalStatus: null,
                PayPalAmount: null,
                PayPalFee: null,
                PayPalDate: null,
                OrderId: order.Id,
                OrderPaymentStatus: order.PaymentStatus.ToString(),
                OrderTotal: order.Total(),
                MatchState: "InEShopNotInPayPal"));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {transactions.Count} PayPal txns, {matchedCount} matched, " +
            $"{payPalOnlyCount} PayPal-only, {eShopOnlyCount} eShop-only.");

        return new ReconciliationReport(
            From: from,
            To: to,
            Currency: _gateway.ConfiguredCurrency,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: matchedCount,
            InPayPalNotInEShopCount: payPalOnlyCount,
            InEShopNotInPayPalCount: eShopOnlyCount,
            Rows: rows);
    }

    private static void AddId(IDictionary<string, Order> map, string? id, Order order)
    {
        if (!string.IsNullOrEmpty(id))
        {
            map[id!] = order;
        }
    }
}
