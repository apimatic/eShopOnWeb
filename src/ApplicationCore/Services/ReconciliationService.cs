using System;
using System.Collections.Generic;
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
    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPalClient, IRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's own record across the whole range (chunked + fully paged inside the client).
        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);

        // The eShop side: orders captured within the range.
        var capturedOrders = await _orderRepository
            .ListAsync(new CapturedOrdersByCaptureDateSpecification(from, to), cancellationToken);

        var ordersByInvoice = capturedOrders
            .Where(o => o.Payment?.InvoiceId is not null)
            .GroupBy(o => o.Payment!.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();
        var matchedCount = 0;
        var payPalOnlyCount = 0;

        foreach (var txn in transactions)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId) && ordersByInvoice.TryGetValue(txn.InvoiceId!, out var found))
            {
                order = found;
            }
            else
            {
                var orderId = OrderInvoice.TryGetOrderId(txn.InvoiceId);
                if (orderId is int id)
                {
                    order = capturedOrders.FirstOrDefault(o => o.Id == id);
                }
            }

            if (order is not null)
            {
                matchedOrderIds.Add(order.Id);
                matchedCount++;
                lines.Add(new ReconciliationLine(
                    Kind: "Matched",
                    PayPalTransactionId: txn.TransactionId,
                    PayPalStatus: txn.Status,
                    PayPalAmount: txn.Amount,
                    Currency: txn.CurrencyCode,
                    InvoiceId: txn.InvoiceId,
                    OrderId: order.Id,
                    OrderStatus: order.Status.ToString(),
                    OrderCapturedAmount: order.Payment?.CapturedGrossAmount));
            }
            else
            {
                payPalOnlyCount++;
                lines.Add(new ReconciliationLine(
                    Kind: "PayPalOnly",
                    PayPalTransactionId: txn.TransactionId,
                    PayPalStatus: txn.Status,
                    PayPalAmount: txn.Amount,
                    Currency: txn.CurrencyCode,
                    InvoiceId: txn.InvoiceId,
                    OrderId: null,
                    OrderStatus: null,
                    OrderCapturedAmount: null));
            }
        }

        // eShop orders PayPal has no transaction for (in range).
        var eShopOnly = capturedOrders.Where(o => !matchedOrderIds.Contains(o.Id)).ToList();
        foreach (var order in eShopOnly)
        {
            lines.Add(new ReconciliationLine(
                Kind: "EShopOnly",
                PayPalTransactionId: null,
                PayPalStatus: null,
                PayPalAmount: null,
                Currency: order.Payment?.CurrencyCode,
                InvoiceId: order.Payment?.InvoiceId,
                OrderId: order.Id,
                OrderStatus: order.Status.ToString(),
                OrderCapturedAmount: order.Payment?.CapturedGrossAmount));
        }

        return new ReconciliationReport(from, to, lines, matchedCount, payPalOnlyCount, eShopOnly.Count);
    }
}
