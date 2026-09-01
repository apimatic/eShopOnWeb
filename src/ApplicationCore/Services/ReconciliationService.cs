using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own record of transactions (transaction search) against eShop
/// payments over a date range. The whole range is covered (all result pages), so a
/// transaction known to only one side shows up as unmatched.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    private readonly IPaymentProcessor _paymentProcessor;
    private readonly IRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPaymentProcessor paymentProcessor, IRepository<OrderPayment> paymentRepository)
    {
        _paymentProcessor = paymentProcessor;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentRequestValidationException("'to' must be after 'from'.");
        }
        if (to - from > MaxRange)
        {
            throw new PaymentRequestValidationException("The reconciliation range cannot exceed 31 days.");
        }

        var transactions = await _paymentProcessor.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsInDateRangeSpec(from, to), cancellationToken);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            GeneratedAt = DateTimeOffset.UtcNow,
            PayPalTransactionCount = transactions.Count
        };

        var matchedPaymentIds = new HashSet<int>();
        var payPalIdsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                payPalIdsSeen.Add(txn.TransactionId);
            }
            if (!string.IsNullOrEmpty(txn.ReferenceId))
            {
                payPalIdsSeen.Add(txn.ReferenceId);
            }

            var match = FindMatch(txn, payments);
            var entry = new ReconciliationEntry
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.ReferenceId,
                PayPalReferenceIdType = txn.ReferenceIdType,
                TransactionEventCode = txn.EventCode,
                TransactionStatus = txn.Status,
                TransactionAmount = txn.Amount,
                Currency = txn.Currency,
                FeeAmount = txn.FeeAmount,
                InvoiceId = txn.InvoiceId,
                CustomField = txn.CustomField,
                TransactionInitiatedAt = txn.InitiatedAt
            };

            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
                entry.MatchStatus = ReconciliationEntry.Matched;
                entry.OrderId = match.OrderId;
                entry.PaymentId = match.Id;
                entry.PaymentStatus = match.Status.ToString();
                entry.PaymentAmount = match.Amount;
            }
            else
            {
                entry.MatchStatus = ReconciliationEntry.MissingInEShop;
                report.MissingInEShopCount++;
            }

            report.Entries.Add(entry);
        }

        foreach (var payment in payments)
        {
            if (matchedPaymentIds.Contains(payment.Id))
            {
                continue;
            }

            var knownIds = new[] { payment.PayPalAuthorizationId, payment.PayPalCaptureId, payment.PayPalOrderId }
                .Concat(payment.Refunds.Select(r => r.PayPalRefundId))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            var anyKnownToPayPal = knownIds.Any(id => payPalIdsSeen.Contains(id!));
            if (anyKnownToPayPal)
            {
                continue;
            }

            report.MissingInPayPalCount++;
            report.Entries.Add(new ReconciliationEntry
            {
                MatchStatus = ReconciliationEntry.MissingInPayPal,
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                PaymentStatus = payment.Status.ToString(),
                PaymentAmount = payment.Amount,
                Currency = payment.Currency,
                PayPalTransactionId = payment.PayPalCaptureId ?? payment.PayPalAuthorizationId ?? payment.PayPalOrderId
            });
        }

        report.MatchedCount = report.Entries.Count(e => e.MatchStatus == ReconciliationEntry.Matched);
        report.Entries = report.Entries
            .OrderBy(e => e.TransactionInitiatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.PaymentId)
            .ToList();
        return report;
    }

    private static OrderPayment? FindMatch(ProcessorTransaction txn, IReadOnlyList<OrderPayment> payments)
    {
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId) &&
                (string.Equals(txn.TransactionId, payment.PayPalCaptureId, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(txn.TransactionId, payment.PayPalAuthorizationId, StringComparison.OrdinalIgnoreCase)
                 || payment.Refunds.Any(r => string.Equals(txn.TransactionId, r.PayPalRefundId, StringComparison.OrdinalIgnoreCase))))
            {
                return payment;
            }

            if (string.Equals(txn.ReferenceIdType, "ODR", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(txn.ReferenceId)
                && string.Equals(txn.ReferenceId, payment.PayPalOrderId, StringComparison.OrdinalIgnoreCase))
            {
                return payment;
            }
        }
        return null;
    }
}
