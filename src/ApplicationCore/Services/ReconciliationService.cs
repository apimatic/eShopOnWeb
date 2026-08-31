using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalClient payPal, IRepository<Payment> paymentRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentDomainException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpec(), cancellationToken);

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var transaction in transactions)
        {
            var payment = FindPayment(payments, transaction);
            entries.Add(new ReconciliationEntry(
                transaction.TransactionId,
                transaction.EventCode,
                transaction.Status,
                transaction.Amount,
                transaction.Currency,
                transaction.FeeAmount,
                transaction.InitiatedAt,
                transaction.InvoiceId,
                payment?.OrderId,
                payment?.Id,
                payment is null ? "paypal-only" : "matched"));

            if (payment is not null)
            {
                matchedPaymentIds.Add(payment.Id);
            }
        }

        // eShop payments that moved money in the range but PayPal's report doesn't know about.
        var missing = new List<UnmatchedPayment>();
        foreach (var payment in payments)
        {
            if (matchedPaymentIds.Contains(payment.Id))
            {
                continue;
            }

            var touchedInRange =
                (payment.CreatedAt >= from && payment.CreatedAt <= to) ||
                (payment.CapturedAt.HasValue && payment.CapturedAt.Value >= from && payment.CapturedAt.Value <= to) ||
                payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
            var hasPayPalState = payment.AuthorizationId is not null || payment.CaptureId is not null || payment.Refunds.Count > 0;

            if (touchedInRange && hasPayPalState)
            {
                missing.Add(new UnmatchedPayment(
                    payment.Id,
                    payment.OrderId,
                    payment.Status.ToString(),
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    payment.Refunds.Select(r => r.PayPalRefundId).ToList()));
            }
        }

        return new ReconciliationReport(from, to, transactions.Count, entries, missing);
    }

    private static Payment? FindPayment(IReadOnlyList<Payment> payments, PayPalTransactionRecord transaction)
    {
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId == transaction.TransactionId ||
                payment.CaptureId == transaction.TransactionId ||
                payment.PayPalOrderId == transaction.TransactionId ||
                payment.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId))
            {
                return payment;
            }
        }

        // A capture's PayPal reference id is usually the authorization it settled.
        if (!string.IsNullOrEmpty(transaction.ReferenceId))
        {
            foreach (var payment in payments)
            {
                if (payment.AuthorizationId == transaction.ReferenceId ||
                    payment.CaptureId == transaction.ReferenceId)
                {
                    return payment;
                }
            }
        }

        // Fall back to the unique invoice id stamped on the purchase unit.
        if (!string.IsNullOrEmpty(transaction.InvoiceId))
        {
            foreach (var payment in payments)
            {
                if (payment.InvoiceId == transaction.InvoiceId)
                {
                    return payment;
                }
            }
        }

        return null;
    }
}
