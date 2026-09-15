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
/// Lines PayPal's own transaction records (over the whole date range) up against eShop orders, keyed
/// by the payment reference we pass to PayPal as the invoice id. Surfaces three buckets: matched,
/// present in PayPal but not eShop, and present in eShop but not PayPal.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPaymentGateway paymentGateway,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop orders that carry a payment reference (i.e. have been authorized/captured).
        var allOrders = await _orderRepository.ListAsync(new OrdersWithItemsSpecification(), cancellationToken);
        var paidOrders = allOrders.Where(o => o.Payment is not null).ToList();

        // Index our side by the payment reference we hand to PayPal as invoice_id/custom_id.
        var ordersByReference = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in paidOrders)
        {
            var reference = order.Payment!.PaymentReference;
            if (!string.IsNullOrEmpty(reference))
            {
                ordersByReference[reference] = order;
            }
        }

        var matched = new List<ReconciliationLine>();
        var payPalOnly = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            var reference = FirstNonEmpty(txn.InvoiceId, txn.CustomField);
            Order? order = null;
            if (reference is not null)
            {
                ordersByReference.TryGetValue(reference, out order);
            }

            if (order is not null)
            {
                matchedReferences.Add(order.Payment!.PaymentReference);
                matched.Add(new ReconciliationLine(
                    order.Payment!.PaymentReference, txn.TransactionId, txn.EventCode, txn.Status,
                    txn.Amount, txn.Currency ?? order.Payment!.Currency, txn.InitiationDate,
                    order.Id, order.Status.ToString(), order.Payment!.Amount,
                    "Matched: PayPal transaction is backed by an eShop order."));
            }
            else
            {
                payPalOnly.Add(new ReconciliationLine(
                    reference, txn.TransactionId, txn.EventCode, txn.Status,
                    txn.Amount, txn.Currency, txn.InitiationDate,
                    null, null, null,
                    "In PayPal but not eShop: no eShop order matches this transaction's reference."));
            }
        }

        // eShop orders authorized/captured within the window that PayPal did not report.
        var eShopOnly = new List<ReconciliationLine>();
        foreach (var order in paidOrders)
        {
            var payment = order.Payment!;
            if (matchedReferences.Contains(payment.PaymentReference))
            {
                continue;
            }

            var activity = payment.CapturedAt ?? payment.CreatedAt;
            if (activity < from || activity > to)
            {
                continue;
            }

            eShopOnly.Add(new ReconciliationLine(
                payment.PaymentReference, null, null, null, null, payment.Currency, null,
                order.Id, order.Status.ToString(), payment.Amount,
                "In eShop but not PayPal: an eShop payment PayPal has not reported (its reporting can lag by up to 3 hours)."));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {transactions.Count} PayPal txns, {paidOrders.Count} eShop paid orders, " +
            $"{matched.Count} matched, {payPalOnly.Count} PayPal-only, {eShopOnly.Count} eShop-only.");

        return new ReconciliationReport(from, to, transactions.Count, paidOrders.Count,
            matched, payPalOnly, eShopOnly);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
