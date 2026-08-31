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
    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalClient payPalClient, IRepository<Payment> paymentRepository)
    {
        _payPalClient = payPalClient;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("The 'to' date must not be earlier than the 'from' date.");
        }

        var payPalTransactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsInRangeSpec(from, to), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };

        // Index every PayPal-owned id eShop knows about: holds, captures and refunds.
        var knownIds = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.AuthorizationId))
            {
                knownIds[payment.AuthorizationId] = payment;
            }
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                knownIds[payment.CaptureId] = payment;
            }
            foreach (var refund in payment.Refunds)
            {
                knownIds[refund.PayPalRefundId] = payment;
            }
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in payPalTransactions)
        {
            seenIds.Add(transaction.TransactionId);
            var matched = knownIds.TryGetValue(transaction.TransactionId, out var payment);
            report.Entries.Add(new ReconciliationEntry
            {
                PayPalTransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                PayPalStatus = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                FeeAmount = transaction.FeeAmount,
                TransactionDate = transaction.InitiationDate,
                OrderId = matched ? payment!.OrderId : null,
                PaymentId = matched ? payment!.Id : null,
                MatchStatus = matched ? "Matched" : "OnlyInPayPal"
            });
        }

        // eShop records PayPal did not report for this range (e.g. reporting lag).
        foreach (var payment in payments)
        {
            AddMissing(report, payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, payment, payment.CreatedAt, seenIds);
            AddMissing(report, payment.CaptureId, payment.CapturedAmount, payment.Currency, payment, payment.CapturedAt, seenIds);
            foreach (var refund in payment.Refunds)
            {
                AddMissing(report, refund.PayPalRefundId, refund.Amount, refund.Currency, payment, refund.CreatedAt, seenIds);
            }
        }

        report.Entries = report.Entries
            .OrderBy(e => e.TransactionDate ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.PayPalTransactionId)
            .ToList();

        return report;
    }

    private static void AddMissing(ReconciliationReport report, string? payPalId, decimal? amount, string? currency,
        Payment payment, DateTimeOffset? date, HashSet<string> seenIds)
    {
        if (string.IsNullOrEmpty(payPalId) || seenIds.Contains(payPalId))
        {
            return;
        }
        seenIds.Add(payPalId);
        report.Entries.Add(new ReconciliationEntry
        {
            PayPalTransactionId = payPalId,
            Amount = amount,
            Currency = currency,
            TransactionDate = date,
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            MatchStatus = "OnlyInEShop"
        });
    }
}
