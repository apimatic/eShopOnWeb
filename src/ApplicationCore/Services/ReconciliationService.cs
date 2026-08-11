using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly ITransactionReporting _reporting;
    private readonly IReadRepository<OrderPayment> _payments;

    public ReconciliationService(ITransactionReporting reporting, IReadRepository<OrderPayment> payments)
    {
        _reporting = reporting;
        _payments = payments;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must not be earlier than 'from'.");
        }

        var transactions = await _reporting.ListTransactionsAsync(from, to, cancellationToken);
        var allPayments = await _payments.ListAsync(cancellationToken);

        // Line PayPal's record up against eShop orders by the reference we stamp as invoice_id.
        var paymentsByReference = allPayments
            .GroupBy(p => p.Reference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Every PayPal transaction: matched to an order, or PayPal-only.
        foreach (var t in transactions)
        {
            OrderPayment? matched = null;
            if (!string.IsNullOrEmpty(t.InvoiceId))
            {
                paymentsByReference.TryGetValue(t.InvoiceId!, out matched);
            }
            if (matched is null && !string.IsNullOrEmpty(t.CustomField))
            {
                paymentsByReference.TryGetValue(t.CustomField!, out matched);
            }

            if (matched is not null)
            {
                matchedReferences.Add(matched.Reference);
                lines.Add(new ReconciliationLine("Matched", t.TransactionId, t.EventCode, t.Status,
                    t.Amount, t.Currency, t.Fee, t.InvoiceId,
                    matched.OrderId, matched.Status.ToString(), matched.Amount, t.Date));
            }
            else
            {
                lines.Add(new ReconciliationLine("PayPalOnly", t.TransactionId, t.EventCode, t.Status,
                    t.Amount, t.Currency, t.Fee, t.InvoiceId,
                    null, null, null, t.Date));
            }
        }

        // 2. eShop orders whose money moved (captured/refunded) but that PayPal's report for this
        //    range does not list — the reverse mismatch.
        foreach (var p in allPayments.Where(HasSettledMoney))
        {
            if (matchedReferences.Contains(p.Reference))
            {
                continue;
            }
            lines.Add(new ReconciliationLine("EShopOnly", null, null, null,
                null, p.CurrencyCode, null, p.Reference,
                p.OrderId, p.Status.ToString(), p.Amount, null));
        }

        var matchedCount = lines.Count(l => l.MatchState == "Matched");
        var payPalOnly = lines.Count(l => l.MatchState == "PayPalOnly");
        var eShopOnly = lines.Count(l => l.MatchState == "EShopOnly");

        return new ReconciliationReport(from, to, matchedCount, payPalOnly, eShopOnly, lines);
    }

    private static bool HasSettledMoney(OrderPayment p) =>
        p.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;
}
