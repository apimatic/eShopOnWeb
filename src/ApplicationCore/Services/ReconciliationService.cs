using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own transaction records for a date range against eShop orders, so a payment
/// PayPal knows about but eShop doesn't — or the reverse — is visible. Matching is by the
/// per-order <see cref="Order.PaymentReference"/>, which the integration stamps onto each PayPal
/// order's invoice id.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    // PayPal's transaction search supports at most a 31-day window.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Order> orderRepository,
        IReadRepository<Payment> paymentRepository, IPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
            throw new ArgumentException("'to' must be later than 'from'.");
        if (to - from > MaxRange)
            throw new ArgumentException("The reconciliation range must not exceed 31 days (PayPal's limit).");

        var paypal = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop side: orders placed in the range that reached PayPal (have a PayPal order id), keyed by
        // their payment reference (what we stamp as PayPal's invoice id).
        var payments = await _paymentRepository.ListAsync(cancellationToken);
        var paidOrderIds = payments
            .Where(p => p.PayPalOrderId is not null)
            .Select(p => p.OrderId)
            .ToHashSet();

        var orders = await _orderRepository.ListAsync(cancellationToken);
        var eshopByRef = orders
            .Where(o => paidOrderIds.Contains(o.Id) && o.OrderDate >= from && o.OrderDate <= to)
            .ToDictionary(o => o.PaymentReference.ToString(), o => o.Id);

        var entries = new List<ReconciliationEntry>();
        var seenRefs = new HashSet<string>();

        foreach (var tx in paypal.Transactions)
        {
            var reference = tx.InvoiceId ?? tx.CustomField;
            int? matchedOrderId = null;
            var state = "PayPalOnly";
            if (reference is not null && eshopByRef.TryGetValue(reference, out var orderId))
            {
                matchedOrderId = orderId;
                state = "Matched";
                seenRefs.Add(reference);
            }

            entries.Add(new ReconciliationEntry(tx.TransactionId, tx.Status, tx.Amount, tx.Currency,
                tx.InvoiceId, matchedOrderId, state));
        }

        // eShop orders PayPal has not (yet) reported — expected during sandbox reporting lag.
        foreach (var (reference, orderId) in eshopByRef)
        {
            if (seenRefs.Contains(reference)) continue;
            entries.Add(new ReconciliationEntry(null, null, null, null, reference, orderId, "EShopOnly"));
        }

        return new ReconciliationReport(from, to, entries,
            PayPalTransactionCount: paypal.Transactions.Count,
            EShopOrderCount: eshopByRef.Count,
            Truncated: paypal.Truncated,
            PagesFetched: paypal.PagesFetched,
            TotalPages: paypal.TotalPages);
    }
}
