using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Lines PayPal's own record of transactions up against eShop orders over a date range, in both
/// directions — a payment PayPal reports that eShop does not reference, and an eShop order that
/// expects a payment PayPal does not report, are each visible.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway gateway,
        ILogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // PayPal's own view over the whole range (all 31-day windows, all pages).
        var payPal = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's view: orders with a payment placed in the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken);
        var ordersByReference = orders.ToDictionary(o => OrderReference.For(o.Id), o => o);

        var entries = new List<ReconciliationEntry>();
        var referencedOrderIds = new HashSet<int>();

        // Direction 1: every PayPal transaction, matched to an eShop order where possible.
        foreach (var tx in payPal.Transactions)
        {
            var reference = ResolveReference(tx.InvoiceId) ?? ResolveReference(tx.CustomField);
            Order? order = null;
            if (reference is not null) ordersByReference.TryGetValue(reference, out order);

            if (order is not null)
            {
                referencedOrderIds.Add(order.Id);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.Matched,
                    tx.TransactionId, reference, order.Id,
                    tx.Amount, order.Payment?.CapturedGross ?? order.Payment?.Amount,
                    tx.Currency ?? order.Payment?.Currency, tx.Status, tx.InitiationDate));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.InPayPalOnly,
                    tx.TransactionId, reference, null,
                    tx.Amount, null, tx.Currency, tx.Status, tx.InitiationDate));
            }
        }

        // Direction 2: eShop orders that expected a payment PayPal did not report in this range.
        foreach (var order in orders.Where(o => !referencedOrderIds.Contains(o.Id)))
        {
            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.InEShopOnly,
                null, OrderReference.For(order.Id), order.Id,
                null, order.Payment?.CapturedGross ?? order.Payment?.Amount,
                order.Payment?.Currency, order.Status.ToString(), order.OrderDate));
        }

        var report = new ReconciliationReport(
            from, to, entries,
            entries.Count(e => e.Match == ReconciliationMatch.Matched),
            entries.Count(e => e.Match == ReconciliationMatch.InPayPalOnly),
            entries.Count(e => e.Match == ReconciliationMatch.InEShopOnly),
            payPal.Complete);

        _logger.LogInformation(
            "Reconciliation {From}..{To}: {Matched} matched, {PayPalOnly} PayPal-only, {EShopOnly} eShop-only, complete={Complete}.",
            from, to, report.MatchedCount, report.InPayPalOnlyCount, report.InEShopOnlyCount, report.PayPalDataComplete);
        return report;
    }

    private static string? ResolveReference(string? value)
        => OrderReference.TryParseOrderId(value, out var id) ? OrderReference.For(id) : null;
}
