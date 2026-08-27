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

public class ReconciliationService : IReconciliationService
{
    // PayPal's transaction search supports a maximum range of 31 days per call.
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);
    private const int PageSize = 100;

    private readonly IPaymentGateway _gateway;
    private readonly IRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new PaymentConflictException("'to' must be after 'from'.");

        var transactions = new List<GatewayTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxWindow < to ? windowStart + MaxWindow : to;

            var page = 1;
            while (true)
            {
                var result = await _gateway.SearchTransactionsAsync(windowStart, windowEnd, page, PageSize, cancellationToken);
                transactions.AddRange(result.Transactions);
                if (page >= result.TotalPages || result.Transactions.Count == 0)
                    break;
                page++;
            }

            windowStart = windowEnd;
        }

        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpec(from, to), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = FindMatchingPayment(txn, payments);
            if (match != null)
                matchedPaymentIds.Add(match.Id);

            report.Transactions.Add(new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                ReferenceId = txn.ReferenceId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                Fee = txn.Fee,
                Time = txn.Time,
                MatchStatus = match != null ? "Matched" : "PayPalOnly",
                OrderId = match?.OrderId,
                PaymentId = match?.Id
            });
        }

        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id) && HasProviderActivity(p)))
        {
            report.EshopOnlyPayments.Add(new EshopOnlyPayment
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                Status = payment.Status.ToString(),
                Amount = payment.Amount,
                Currency = payment.Currency,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId
            });
        }

        report.TotalPayPalTransactions = report.Transactions.Count;
        report.MatchedCount = report.Transactions.Count(t => t.MatchStatus == "Matched");
        report.PayPalOnlyCount = report.Transactions.Count(t => t.MatchStatus == "PayPalOnly");
        return report;
    }

    private static Payment? FindMatchingPayment(GatewayTransaction txn, IReadOnlyList<Payment> payments)
    {
        return payments.FirstOrDefault(p =>
            p.PayPalOrderId == txn.TransactionId ||
            p.AuthorizationId == txn.TransactionId ||
            p.CaptureId == txn.TransactionId ||
            p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
            (txn.ReferenceId != null &&
                (p.PayPalOrderId == txn.ReferenceId || p.AuthorizationId == txn.ReferenceId || p.CaptureId == txn.ReferenceId)) ||
            (txn.CustomField != null && txn.CustomField == $"eshop-order-{p.OrderId}"));
    }

    private static bool HasProviderActivity(Payment payment)
        => payment.PayPalOrderId != null || payment.AuthorizationId != null || payment.CaptureId != null;
}
