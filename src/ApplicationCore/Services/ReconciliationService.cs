using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(
        IPayPalGateway payPal,
        IReadRepository<Payment> paymentRepository,
        IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's own record across the WHOLE range (the gateway walks every page).
        var payPalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);

        // eShop's record: payments whose order falls in the range.
        var payments = await _paymentRepository.ListAsync(new AllPaymentsSpecification(), cancellationToken);
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var orderDates = orders.ToDictionary(o => o.Id, o => o.OrderDate);

        var eShopInRange = payments
            .Where(p => orderDates.TryGetValue(p.OrderId, out var date) && date >= from && date <= to)
            .ToList();

        var lines = new List<ReconciliationLine>();

        // Group PayPal transactions by the eShop order reference we stamped on them.
        var payPalByReference = payPalTransactions
            .Where(t => !string.IsNullOrEmpty(t.OrderReference))
            .GroupBy(t => t.OrderReference!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchedReferences = new HashSet<string>();

        // eShop side: match each payment to PayPal's transactions by order reference.
        foreach (var payment in eShopInRange)
        {
            var reference = payment.OrderId.ToString();
            var eShopAmount = payment.CapturedAmount ?? payment.Amount;

            if (payPalByReference.TryGetValue(reference, out var txns))
            {
                matchedReferences.Add(reference);
                var primary = txns[0];
                lines.Add(new ReconciliationLine(
                    Match: ReconciliationMatch.Matched,
                    OrderId: payment.OrderId,
                    PayPalTransactionId: string.Join(",", txns.Select(t => t.TransactionId)),
                    PayPalStatus: primary.Status,
                    PayPalAmount: txns.Sum(t => t.Amount),
                    EShopStatus: payment.Status.ToString(),
                    EShopAmount: eShopAmount,
                    CurrencyCode: payment.CurrencyCode));
            }
            else
            {
                // PayPal has not reported it (e.g. reporting lag) — visible, not silently dropped.
                lines.Add(new ReconciliationLine(
                    Match: ReconciliationMatch.EShopOnly,
                    OrderId: payment.OrderId,
                    PayPalTransactionId: null,
                    PayPalStatus: null,
                    PayPalAmount: null,
                    EShopStatus: payment.Status.ToString(),
                    EShopAmount: eShopAmount,
                    CurrencyCode: payment.CurrencyCode));
            }
        }

        // PayPal side: transactions with no matching eShop order (including those with no reference).
        foreach (var transaction in payPalTransactions)
        {
            var reference = transaction.OrderReference;
            var isMatched = !string.IsNullOrEmpty(reference) && matchedReferences.Contains(reference);
            if (isMatched)
            {
                continue;
            }

            lines.Add(new ReconciliationLine(
                Match: ReconciliationMatch.PayPalOnly,
                OrderId: int.TryParse(reference, out var oid) ? oid : (int?)null,
                PayPalTransactionId: transaction.TransactionId,
                PayPalStatus: transaction.Status,
                PayPalAmount: transaction.Amount,
                EShopStatus: null,
                EShopAmount: null,
                CurrencyCode: transaction.CurrencyCode));
        }

        return new ReconciliationReport(from, to, lines)
        {
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.EShopOnly)
        };
    }
}
