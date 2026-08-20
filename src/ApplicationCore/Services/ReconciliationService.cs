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
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IReadRepository<Order> _orders;

    public ReconciliationService(IPayPalPaymentsClient payPal, IReadRepository<Order> orders)
    {
        _payPal = payPal;
        _orders = orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orders.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        var ordersByPayPalId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in eshopOrders)
        {
            foreach (var id in order.PayPalIdentifiers())
            {
                ordersByPayPalId.TryAdd(id, order);
            }

            var invoiceId = InvoiceIdFor(order);
            ordersByPayPalId.TryAdd(invoiceId, order);
            ordersByPayPalId.TryAdd(order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), order);
        }

        var matchedOrderIds = new HashSet<int>();
        var rows = new List<ReconciliationMatch>(paypalTransactions.Count);

        foreach (var txn in paypalTransactions)
        {
            Order? match = null;
            if (!string.IsNullOrWhiteSpace(txn.TransactionId))
            {
                ordersByPayPalId.TryGetValue(txn.TransactionId, out match);
            }

            if (match is null && !string.IsNullOrWhiteSpace(txn.PaypalReferenceId))
            {
                ordersByPayPalId.TryGetValue(txn.PaypalReferenceId, out match);
            }

            if (match is null && !string.IsNullOrWhiteSpace(txn.InvoiceId))
            {
                ordersByPayPalId.TryGetValue(txn.InvoiceId, out match);
                if (match is null)
                {
                    match = eshopOrders.FirstOrDefault(o =>
                        o.Payment?.InvoiceId is not null
                        && string.Equals(o.Payment.InvoiceId, txn.InvoiceId, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (match is null && !string.IsNullOrWhiteSpace(txn.CustomField))
            {
                ordersByPayPalId.TryGetValue(txn.CustomField, out match);
            }

            if (match is not null)
            {
                matchedOrderIds.Add(match.Id);
            }

            rows.Add(new ReconciliationMatch(
                txn,
                match,
                match is null ? "paypal_only" : "matched"));
        }

        var eshopOnly = eshopOrders
            .Where(o => o.Payment is not null)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .ToList();

        return new ReconciliationReport(from, to, rows, eshopOnly);
    }

    public static string InvoiceIdFor(Order order) =>
        $"ESHOP-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";
}
