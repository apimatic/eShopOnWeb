using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPalClient;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalClient payPalClient)
    {
        _orderRepository = orderRepository;
        _payPalClient = payPalClient;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.");

        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // Index eShop orders by the invoice id we sent to PayPal when authorizing.
        var ordersByInvoice = orders
            .Where(o => o.Payment is not null && !string.IsNullOrEmpty(o.Payment.InvoiceId))
            .GroupBy(o => o.Payment!.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedCount = 0;

        foreach (var txn in transactions)
        {
            var key = FirstNonEmpty(txn.InvoiceId, txn.CustomField);
            Order? order = null;
            if (key is not null)
                ordersByInvoice.TryGetValue(key, out order);

            if (order is not null)
            {
                matchedCount++;
                if (key is not null) matchedInvoices.Add(key);
            }

            entries.Add(new ReconciliationEntry(
                TransactionId: txn.TransactionId,
                InvoiceId: txn.InvoiceId ?? txn.CustomField,
                PayPalAmount: txn.Amount,
                PayPalStatus: txn.Status,
                TransactionDate: txn.InitiationDate,
                OrderId: order?.Id,
                OrderAmount: order?.Payment is null ? null : order.Payment.AuthorizedAmount,
                OrderState: order?.State.ToString(),
                Status: order is not null ? "MATCHED" : "IN_PAYPAL_NOT_ESHOP"));
        }

        // eShop orders PayPal has no transaction for in this range.
        foreach (var kvp in ordersByInvoice)
        {
            if (matchedInvoices.Contains(kvp.Key)) continue;
            var order = kvp.Value;
            entries.Add(new ReconciliationEntry(
                TransactionId: null,
                InvoiceId: kvp.Key,
                PayPalAmount: null,
                PayPalStatus: null,
                TransactionDate: null,
                OrderId: order.Id,
                OrderAmount: order.Payment!.AuthorizedAmount,
                OrderState: order.State.ToString(),
                Status: "IN_ESHOP_NOT_PAYPAL"));
        }

        return new ReconciliationReport(from, to, transactions.Count, matchedCount, entries);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
