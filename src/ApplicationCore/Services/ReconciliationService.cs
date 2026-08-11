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

/// <summary>
/// Lines up PayPal's own transaction record against eShop's captured payments for a date range so a payment
/// one side knows about and the other does not becomes visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private const decimal CentTolerance = 0.01m;

    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));

        // PayPal's record for the range (paged and chunked to cover the whole window).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // eShop's record for the range: orders captured within it.
        var orders = await _orderRepository.ListAsync(new OrdersCapturedBetweenSpecification(from, to), ct);

        // Map every PayPal id we hold (capture, authorization, refunds) back to the owning order.
        var idToOrder = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var p = order.Payment!;
            if (p.CaptureId is not null) idToOrder[p.CaptureId] = order;
            if (!string.IsNullOrEmpty(p.AuthorizationId)) idToOrder[p.AuthorizationId] = order;
            foreach (var r in p.Refunds.Where(r => r.PayPalRefundId is not null))
                idToOrder[r.PayPalRefundId!] = order;
        }

        var ordersById = orders.ToDictionary(o => o.Id.ToString());

        var entries = new List<ReconciliationEntry>();
        var eShopCaptureIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            Order? matched = null;
            if (idToOrder.TryGetValue(txn.TransactionId, out var byId))
                matched = byId;
            else if (!string.IsNullOrEmpty(txn.CustomField) && ordersById.TryGetValue(txn.CustomField!, out var byCustom))
                matched = byCustom;

            if (matched is null)
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationStatus.InPayPalOnly, txn.TransactionId, txn.Status, txn.Amount, null, null, null,
                    txn.Currency, txn.Date, AmountsAgree: false));
                continue;
            }

            var payment = matched.Payment!;
            var isCaptureRow = string.Equals(payment.CaptureId, txn.TransactionId, StringComparison.OrdinalIgnoreCase);
            if (isCaptureRow && payment.CaptureId is not null)
                eShopCaptureIdsSeen.Add(payment.CaptureId);

            var amountsAgree = !isCaptureRow
                || (payment.CapturedAmount is { } captured
                    && Math.Abs(Math.Abs(txn.Amount) - captured) <= CentTolerance);

            entries.Add(new ReconciliationEntry(
                ReconciliationStatus.Matched, txn.TransactionId, txn.Status, txn.Amount,
                matched.Id, payment.CaptureId, payment.CapturedAmount, txn.Currency, txn.Date, amountsAgree));
        }

        // eShop captures the PayPal report does not (yet) show.
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.CaptureId is null || eShopCaptureIdsSeen.Contains(payment.CaptureId))
                continue;

            entries.Add(new ReconciliationEntry(
                ReconciliationStatus.InEShopOnly, null, null, null, order.Id, payment.CaptureId,
                payment.CapturedAmount, payment.Currency, payment.CapturedAt, AmountsAgree: false));
        }

        var matchedCount = entries.Count(e => e.Status == ReconciliationStatus.Matched);
        var payPalOnly = entries.Count(e => e.Status == ReconciliationStatus.InPayPalOnly);
        var eShopOnly = entries.Count(e => e.Status == ReconciliationStatus.InEShopOnly);

        return new ReconciliationReport(
            from, to, transactions.Count, orders.Count(o => o.Payment!.CaptureId is not null),
            matchedCount, payPalOnly, eShopOnly, entries);
    }
}
