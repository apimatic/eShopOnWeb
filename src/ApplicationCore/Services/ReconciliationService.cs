using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new PaymentException("'to' must be on or after 'from'.");
        }

        // PayPal's full transaction record for the range (chunked + paginated inside the client).
        var transactions = await _payPal.ListTransactionsAsync(from, to, ct);

        // Every eShop order that carries a payment, keyed by the reference we sent to PayPal.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct);
        var ordersByReference = orders
            .GroupBy(o => o.PaymentReference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotInEShop = new List<ReconciliationMatch>();
        var referencesSeenAtPayPal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            var reference = FirstNonEmpty(txn.InvoiceId, txn.CustomField);
            var match = new ReconciliationMatch(txn.TransactionId, txn.Status, txn.Amount, txn.Currency,
                reference, OrderId: null);

            if (reference is not null && ordersByReference.TryGetValue(reference, out var order))
            {
                referencesSeenAtPayPal.Add(reference);
                matched.Add(match with { OrderId = order.Id });
            }
            else
            {
                inPayPalNotInEShop.Add(match);
            }
        }

        // eShop orders whose money actually moved (captured) within the range but which PayPal's
        // report does not (yet) show. Reporting can lag live activity by up to a few hours, so for a
        // very recent range this can legitimately be non-empty without indicating a real discrepancy.
        var inEShopNotInPayPal = orders
            .Where(o => o.Payment is not null
                && o.Payment.CapturedAt is { } capturedAt
                && capturedAt >= from && capturedAt <= to
                && !referencesSeenAtPayPal.Contains(o.PaymentReference))
            .Select(o => new UnmatchedOrder(
                o.Id,
                o.PaymentReference,
                o.Payment!.CapturedAmount ?? o.Payment.AuthorizedAmount,
                o.Payment.Currency,
                o.Status.ToString(),
                o.Payment.CapturedAt))
            .ToList();

        return new ReconciliationReport(from, to, matched, inPayPalNotInEShop, inEShopNotInPayPal,
            transactions.Count);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
