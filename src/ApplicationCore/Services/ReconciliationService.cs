using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's own record across the whole range (the gateway walks 31-day windows and all pages).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's side: every order that has moved (or holds) money.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        // Join on the globally-unique invoice id eShop stamps onto each PayPal order/capture. (The
        // order's own id also travels as PayPal's custom_field, but it is not a reliable join key:
        // it is only unique within a single run, so it is surfaced for information, not matched on.)
        var byInvoice = orders
            .Where(o => o.Payment is not null)
            .ToDictionary(o => o.Payment!.InvoiceId, o => o, StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<ReconciliationMatch>();
        var matchedInvoiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId) && byInvoice.TryGetValue(tx.InvoiceId, out var byInv))
            {
                order = byInv;
            }

            var line = new ReconciliationMatch(tx.TransactionId, tx.Status, tx.Amount, tx.Currency,
                tx.Date, tx.InvoiceId, order?.Id);

            if (order is not null)
            {
                matched.Add(line);
                if (order.Payment is not null)
                {
                    matchedInvoiceIds.Add(order.Payment.InvoiceId);
                }
            }
            else
            {
                inPayPalOnly.Add(line);
            }
        }

        // eShop payments that actually took money but which PayPal's records did not report over the
        // range. (Recent activity legitimately absent due to PayPal's reporting lag.)
        var inEShopOnly = orders
            .Where(o => o.Payment is { CaptureId: not null } payment
                && !matchedInvoiceIds.Contains(payment.InvoiceId))
            .Select(o => new EShopPaymentRecord(
                o.Id,
                o.Payment!.InvoiceId,
                o.Payment.PayPalOrderId,
                o.Payment.CaptureId,
                o.Payment.CapturedGross ?? o.Payment.Amount,
                o.Payment.Currency,
                o.Payment.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched, inPayPalOnly, inEShopOnly);
    }
}
