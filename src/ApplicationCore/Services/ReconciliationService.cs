using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Payment> paymentRepository)
    {
        _paymentGateway = paymentGateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to);
        var localPayments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpec(from, to));

        var report = new ReconciliationReport { From = from, To = to };

        // Every PayPal-owned id we know about, pointing back at the local payment.
        var localByPayPalId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in localPayments)
        {
            foreach (var id in PayPalIdsOf(payment))
            {
                localByPayPalId[id] = payment;
            }
        }

        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            Payment? matched = null;
            if (txn.TransactionId is not null)
            {
                localByPayPalId.TryGetValue(txn.TransactionId, out matched);
            }
            if (matched is null && txn.ReferenceId is not null)
            {
                localByPayPalId.TryGetValue(txn.ReferenceId, out matched);
            }

            if (matched is not null)
            {
                matchedPaymentIds.Add(matched.Id);
            }

            report.Entries.Add(new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                ReferenceId = txn.ReferenceId,
                ReferenceIdType = txn.ReferenceIdType,
                EventCode = txn.EventCode,
                TransactionStatus = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                FeeAmount = txn.FeeAmount,
                TransactionTime = txn.InitiationTime,
                OrderId = matched?.OrderId,
                PaymentId = matched?.Id,
                MatchStatus = matched is null ? "MissingLocally" : "Matched"
            });
        }

        foreach (var payment in localPayments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            report.Entries.Add(new ReconciliationEntry
            {
                TransactionId = payment.CaptureId ?? payment.AuthorizationId ?? payment.PayPalOrderId,
                ReferenceId = payment.PayPalOrderId,
                ReferenceIdType = "ODR",
                TransactionStatus = payment.Status.ToString(),
                Amount = payment.CapturedAmount ?? payment.Amount,
                Currency = payment.Currency,
                FeeAmount = payment.PayPalFee,
                TransactionTime = payment.CreatedAt,
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                MatchStatus = "MissingInPayPal"
            });
        }

        report.Entries = report.Entries
            .OrderBy(e => e.TransactionTime)
            .ThenBy(e => e.TransactionId)
            .ToList();

        return report;
    }

    private static IEnumerable<string> PayPalIdsOf(Payment payment)
    {
        if (!string.IsNullOrEmpty(payment.PayPalOrderId)) yield return payment.PayPalOrderId;
        if (!string.IsNullOrEmpty(payment.AuthorizationId)) yield return payment.AuthorizationId;
        if (!string.IsNullOrEmpty(payment.CaptureId)) yield return payment.CaptureId;
        foreach (var refund in payment.Refunds)
        {
            if (!string.IsNullOrEmpty(refund.PayPalRefundId)) yield return refund.PayPalRefundId;
        }
    }
}
