using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    // Orders are tagged at PayPal with invoice_id / custom_id of this shape, e.g. "ESHOP-42".
    private const string OrderReferencePrefix = "ESHOP-";

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation 'to' date must not be earlier than the 'from' date.");
        }

        // PayPal's record over the whole range (chunked and paged inside the gateway).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's record: orders that have a PayPal payment and whose order date falls in the range.
        var eShopOrders = (await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken))
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .ToList();
        var eShopById = eShopOrders.ToDictionary(o => o.Id);

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            if (TryParseOrderId(txn.InvoiceId, out var orderId) || TryParseOrderId(txn.CustomField, out orderId))
            {
                if (eShopById.TryGetValue(orderId, out var order))
                {
                    matchedOrderIds.Add(orderId);
                    var orderTotal = order.Total();
                    matched.Add(new ReconciliationMatch(
                        OrderId: orderId,
                        TransactionId: txn.TransactionId,
                        PayPalStatus: txn.Status,
                        PayPalAmount: txn.Amount,
                        EShopStatus: order.Status.ToString(),
                        OrderTotal: orderTotal,
                        AmountsAgree: Math.Abs(txn.Amount) == orderTotal));
                    continue;
                }
            }

            // A transaction PayPal knows about that does not line up with any eShop order in range.
            inPayPalOnly.Add(new ReconciliationPayPalOnly(
                txn.TransactionId, txn.InvoiceId, txn.Amount, txn.Currency, txn.Status, txn.InitiationDate));
        }

        // eShop orders with payment activity that PayPal's report does not show in this range.
        var inEShopOnly = eShopOrders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEShopOnly(
                o.Id, o.Status.ToString(), o.Total(), o.Payment?.PayPalOrderId, o.Payment?.CaptureId))
            .ToList();

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            EShopOrderCount: eShopOrders.Count,
            Matched: matched,
            InPayPalOnly: inPayPalOnly,
            InEShopOnly: inEShopOnly);
    }

    private static bool TryParseOrderId(string? reference, out int orderId)
    {
        orderId = 0;
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith(OrderReferencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return int.TryParse(reference.AsSpan(OrderReferencePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId);
    }
}
