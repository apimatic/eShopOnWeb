using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Payment> paymentRepository, IPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's own record for the range (covers the whole range, all pages).
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payPalById = payPalTransactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's own money-moving records (captures and refunds) for the range.
        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpecification(from, to), cancellationToken);

        var eShopLines = new List<ReconciliationLine>();
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && InRange(payment.CapturedAt, from, to))
            {
                eShopLines.Add(new ReconciliationLine(
                    payment.CaptureId, "Capture", payment.CapturedAmount, payment.Currency,
                    payment.CaptureStatus, payment.OrderId, payment.CapturedAt));
            }
            foreach (var refund in payment.Refunds.Where(r => InRange(r.CreatedAt, from, to)))
            {
                eShopLines.Add(new ReconciliationLine(
                    refund.RefundId, "Refund", refund.Amount, payment.Currency,
                    refund.Status, payment.OrderId, refund.CreatedAt));
            }
        }
        var eShopById = eShopLines
            .GroupBy(l => l.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationLine>();
        var onlyInPayPal = new List<ReconciliationLine>();

        foreach (var tx in payPalById.Values)
        {
            if (eShopById.TryGetValue(tx.TransactionId, out var local))
            {
                matched.Add(new ReconciliationLine(
                    tx.TransactionId, local.Kind, tx.Amount ?? local.Amount, tx.Currency ?? local.Currency,
                    tx.Status, local.OrderId, tx.InitiationDate ?? local.Date));
            }
            else
            {
                onlyInPayPal.Add(new ReconciliationLine(
                    tx.TransactionId, "Unknown", tx.Amount, tx.Currency, tx.Status, null, tx.InitiationDate));
            }
        }

        var onlyInEShop = eShopLines
            .Where(l => !payPalById.ContainsKey(l.TransactionId))
            .ToList();

        return new ReconciliationReport(from, to, matched, onlyInPayPal, onlyInEShop);
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value is { } v && v >= from && v <= to;
}
