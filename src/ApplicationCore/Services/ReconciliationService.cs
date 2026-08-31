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
    private readonly ITransactionSearch _transactionSearch;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(ITransactionSearch transactionSearch, IRepository<Payment> paymentRepository)
    {
        _transactionSearch = transactionSearch;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _transactionSearch.SearchAsync(from, to, ct);
        var localPayments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpec(from, to), ct);

        var matchedPaymentIds = new HashSet<int>();
        var rows = new List<ReconciliationRow>();

        foreach (var transaction in transactions)
        {
            var match = localPayments.FirstOrDefault(p => Matches(transaction, p));
            string matchState;
            int? matchedOrderId = null;
            int? matchedPaymentId = null;
            if (match != null)
            {
                matchedPaymentIds.Add(match.Id);
                matchedOrderId = match.OrderId;
                matchedPaymentId = match.Id;
                matchState = "Matched";
            }
            else
            {
                matchState = "OnlyInPayPal";
            }

            rows.Add(new ReconciliationRow(
                transaction.TransactionId,
                transaction.PayPalReferenceId,
                transaction.ReferenceIdType,
                transaction.InvoiceId,
                transaction.CustomField,
                transaction.EventCode,
                transaction.Time,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee,
                transaction.Status,
                matchedOrderId,
                matchedPaymentId,
                matchState));
        }

        var unmatchedLocal = localPayments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new UnmatchedLocalPayment(
                p.Id,
                p.OrderId,
                p.BuyerId,
                p.Amount,
                p.Currency,
                p.Status.ToString(),
                p.PayPalOrderId,
                p.AuthorizationId,
                p.CaptureId,
                p.CreatedAt,
                "OnlyInEShop"))
            .ToList();

        return new ReconciliationReport(from, to, rows, unmatchedLocal);
    }

    private static bool Matches(GatewayTransaction transaction, Payment payment)
    {
        if (!string.IsNullOrEmpty(transaction.TransactionId))
        {
            if (transaction.TransactionId == payment.AuthorizationId ||
                transaction.TransactionId == payment.CaptureId ||
                payment.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId))
            {
                return true;
            }
        }
        if (!string.IsNullOrEmpty(transaction.PayPalReferenceId) &&
            transaction.PayPalReferenceId == payment.PayPalOrderId)
        {
            return true;
        }
        if (!string.IsNullOrEmpty(transaction.CustomField) &&
            string.Equals(transaction.CustomField, PaymentInvoiceId.For(payment.OrderId, payment.CreateRequestKey), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(transaction.InvoiceId) &&
            string.Equals(transaction.InvoiceId, PaymentInvoiceId.For(payment.OrderId, payment.CreateRequestKey), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }
}
