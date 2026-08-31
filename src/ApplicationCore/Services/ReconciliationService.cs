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
    private readonly IPaymentGateway _gateway;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpecification(), cancellationToken);

        // Index every processor id eShop knows about: holds, captures and refunds.
        var knownIds = new Dictionary<string, (Payment Payment, string RecordType)>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.AuthorizationId))
            {
                knownIds[payment.AuthorizationId] = (payment, "Authorization");
            }
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                knownIds[payment.CaptureId] = (payment, "Capture");
            }
            foreach (var refund in payment.Refunds)
            {
                knownIds[refund.PayPalRefundId] = (payment, "Refund");
            }
        }

        var matched = new List<ReconciledTransaction>();
        var unmatched = new List<ReconciledTransaction>();
        foreach (var tx in transactions)
        {
            knownIds.TryGetValue(tx.TransactionId, out var match);
            var row = new ReconciledTransaction(
                tx.TransactionId,
                tx.ReferenceId,
                tx.EventCode,
                tx.Status,
                tx.Amount,
                tx.Currency,
                tx.Fee,
                tx.InitiationDate,
                match.Payment?.OrderId,
                match.Payment?.Id,
                match.RecordType);
            (match.Payment == null ? unmatched : matched).Add(row);
        }

        // eShop records PayPal's report does not know about (e.g. reporting lag).
        var reportedIds = new HashSet<string>(transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var missing = new List<MissingPaymentRecord>();
        foreach (var payment in payments)
        {
            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded
                && !string.IsNullOrEmpty(payment.AuthorizationId)
                && !reportedIds.Contains(payment.AuthorizationId))
            {
                missing.Add(new MissingPaymentRecord(payment.OrderId, payment.Id, "Authorization", payment.AuthorizationId, payment.Status.ToString()));
            }
            if (!string.IsNullOrEmpty(payment.CaptureId) && !reportedIds.Contains(payment.CaptureId))
            {
                missing.Add(new MissingPaymentRecord(payment.OrderId, payment.Id, "Capture", payment.CaptureId, payment.Status.ToString()));
            }
            foreach (var refund in payment.Refunds)
            {
                if (!reportedIds.Contains(refund.PayPalRefundId))
                {
                    missing.Add(new MissingPaymentRecord(payment.OrderId, payment.Id, "Refund", refund.PayPalRefundId, payment.Status.ToString()));
                }
            }
        }

        var all = matched.Concat(unmatched).ToList();
        return new ReconciliationReport(from, to, all, unmatched, missing);
    }
}
