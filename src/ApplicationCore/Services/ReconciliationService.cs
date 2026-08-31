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

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var localPayments = await _paymentRepository.ListAsync(new PaymentsUpdatedInRangeSpec(from, to), cancellationToken);

        var transactionsOut = new List<ReconciliationTransaction>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var match = FindMatch(tx, localPayments);
            if (match.payment != null)
            {
                matchedPaymentIds.Add(match.payment.Id);
            }

            transactionsOut.Add(new ReconciliationTransaction(
                tx.TransactionId,
                tx.ReferenceId,
                tx.EventCode,
                tx.Status,
                tx.Amount?.Value,
                tx.Amount?.CurrencyCode,
                tx.FeeAmount?.Value,
                tx.InvoiceId,
                tx.InitiationDate,
                match.payment != null,
                match.payment?.OrderId,
                match.matchType));
        }

        var eshopOnly = localPayments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Where(p => p.AuthorizationId != null || p.CaptureId != null || p.Refunds.Any(r => r.PayPalRefundId != null))
            .Select(p => new ReconciliationLocalPayment(
                p.OrderId,
                p.BuyerId,
                p.Status.ToString(),
                p.AuthorizationId,
                p.CaptureId,
                p.CapturedAmount,
                p.Currency))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            transactions.Count,
            transactionsOut.Count(t => t.MatchedToEshopOrder),
            transactionsOut,
            eshopOnly);
    }

    private static (Payment? payment, string? matchType) FindMatch(GatewayTransaction tx, IReadOnlyList<Payment> localPayments)
    {
        foreach (var payment in localPayments)
        {
            if (payment.CaptureId != null && (tx.TransactionId == payment.CaptureId || tx.ReferenceId == payment.CaptureId))
            {
                return (payment, "capture");
            }
            if (payment.AuthorizationId != null && (tx.TransactionId == payment.AuthorizationId || tx.ReferenceId == payment.AuthorizationId))
            {
                return (payment, "authorization");
            }
            if (payment.Refunds.Any(r => r.PayPalRefundId == tx.TransactionId || r.PayPalRefundId == tx.ReferenceId))
            {
                return (payment, "refund");
            }
        }
        return (null, null);
    }
}
