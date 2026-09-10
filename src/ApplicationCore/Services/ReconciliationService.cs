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
/// Lines PayPal's own transaction records up against eShop orders for a date range, so a payment
/// PayPal knows about that eShop does not — or the reverse — is visible. The PayPal side is fetched
/// across the whole range (all pages) by <see cref="IPayPalPaymentService.ListTransactionsAsync"/>.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalPaymentService payPal, IReadRepository<Order> orders)
    {
        _payPal = payPal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.");

        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orders.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // eShop orders with a capture, keyed by their id (which we send PayPal as custom_id → custom_field).
        var capturedOrders = orders
            .Where(o => o.Payment?.CaptureId != null)
            .ToDictionary(o => o.Id.ToString(), o => o);

        var entries = new List<ReconciliationEntry>();
        var matchedOrderKeys = new HashSet<string>();

        foreach (var tx in transactions)
        {
            var key = tx.CustomField;
            if (key != null && capturedOrders.TryGetValue(key, out var order))
            {
                matchedOrderKeys.Add(key);
                entries.Add(new ReconciliationEntry(
                    tx.TransactionId, tx.Status, tx.Amount, tx.Currency,
                    order.Id, order.Status.ToString(), order.Payment!.CapturedAmount, "Matched"));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    tx.TransactionId, tx.Status, tx.Amount, tx.Currency,
                    null, null, null, "InPayPalNotInEShop"));
            }
        }

        // eShop captures whose transaction PayPal did not return for this range.
        foreach (var (key, order) in capturedOrders)
        {
            if (matchedOrderKeys.Contains(key))
                continue;
            entries.Add(new ReconciliationEntry(
                null, order.Status.ToString(), null, order.Payment!.Currency,
                order.Id, order.Status.ToString(), order.Payment.CapturedAmount, "InEShopNotInPayPal"));
        }

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: entries.Count(e => e.Classification == "Matched"),
            InPayPalNotInEShopCount: entries.Count(e => e.Classification == "InPayPalNotInEShop"),
            InEShopNotInPayPalCount: entries.Count(e => e.Classification == "InEShopNotInPayPal"),
            Entries: entries);
    }
}
