using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGatewayClient _gateway;
    private readonly IRepository<Entities.PaymentAggregate.OrderPayment> _paymentRepository;

    public ReconciliationService(IPaymentGatewayClient gateway, IRepository<Entities.PaymentAggregate.OrderPayment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentException("'to' must be after 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsForReconciliationSpecification(to), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            // Match on the exact PayPal ids we persist: a transaction's own id, or its
            // reference id (captures reference their authorization, refunds their capture).
            var payment = payments.FirstOrDefault(p =>
                p.AuthorizationId == txn.TransactionId ||
                p.CaptureId == txn.TransactionId ||
                p.PayPalOrderId == txn.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
                (txn.ReferenceId is not null &&
                    (p.AuthorizationId == txn.ReferenceId ||
                     p.CaptureId == txn.ReferenceId ||
                     p.PayPalOrderId == txn.ReferenceId ||
                     p.Refunds.Any(r => r.PayPalRefundId == txn.ReferenceId))));

            var entry = new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Fee = txn.Fee,
                Currency = txn.Currency,
                InitiationTime = txn.InitiationTime,
                InvoiceId = txn.InvoiceId
            };

            if (payment is not null)
            {
                entry.Match = "matched";
                entry.MatchedOrderId = payment.OrderId;
                entry.MatchedPaymentId = payment.Id;
                matchedPaymentIds.Add(payment.Id);
            }

            report.Transactions.Add(entry);
        }

        foreach (var payment in payments.Where(p => p.CreatedAt >= from && !matchedPaymentIds.Contains(p.Id)))
        {
            report.PaymentsWithoutPayPalTransaction.Add(new UnmatchedPayment
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                Status = payment.Status.ToString(),
                Amount = payment.Amount,
                Currency = payment.Currency,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                CreatedAt = payment.CreatedAt
            });
        }

        return report;
    }
}
