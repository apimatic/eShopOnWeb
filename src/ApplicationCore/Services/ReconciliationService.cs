using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentService _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalPaymentService payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        // PayPal's own record across the WHOLE range (the service pages through all results).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // Every eShop order that carries a payment, keyed by its unique payment reference so PayPal
        // records line up exactly — a resettable order id would false-match a shared sandbox account.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct);
        var ordersByReference = orders
            .Where(o => o.Payment is not null && !string.IsNullOrEmpty(o.Payment.PaymentReference))
            .ToDictionary(o => o.Payment!.PaymentReference);

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<PayPalOnlyEntry>();
        var matchedReferences = new HashSet<string>();

        foreach (var tx in transactions)
        {
            var reference = tx.CustomField ?? tx.InvoiceId;
            if (reference is not null && ordersByReference.TryGetValue(reference, out var order))
            {
                matchedReferences.Add(reference);
                var amountsAgree = tx.Amount.HasValue && Math.Abs(tx.Amount.Value - order.Total()) < 0.01m;
                matched.Add(new ReconciliationMatch(order.Id, order.Status.ToString(), order.Total(),
                    tx.TransactionId, tx.Status, tx.Amount, amountsAgree));
            }
            else
            {
                // PayPal knows about a transaction eShop cannot line up to an order.
                payPalOnly.Add(new PayPalOnlyEntry(tx.TransactionId, tx.Status, tx.Amount, tx.Currency,
                    tx.InitiationDate, reference));
            }
        }

        // eShop payments in-range that PayPal's report does not (yet) show — expected under sandbox lag.
        var eShopOnly = orders
            .Where(o => o.Payment is not null && !matchedReferences.Contains(o.Payment.PaymentReference))
            .Where(o => PaymentActivityInRange(o.Payment!, from, to))
            .Select(o => new EShopOnlyEntry(o.Id, o.Status.ToString(), o.Total(),
                o.Payment!.PayPalOrderId, o.Payment!.CaptureId))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
    }

    private static bool PaymentActivityInRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        var when = payment.CapturedAt ?? payment.AuthorizedAt;
        return when >= from && when <= to;
    }
}
