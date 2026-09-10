using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the reconciliation report: pulls PayPal's own record of transactions for a date range and
/// lines them up against eShop payments, so a transaction one side knows about and the other doesn't
/// becomes visible. Over a range with no settled data yet (sandbox lag) the report is legitimately empty.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Payment> paymentRepository,
        IPayPalGateway payPal,
        IAppLogger<ReconciliationService> logger)
    {
        _paymentRepository = paymentRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var payments = await _paymentRepository.ListAsync(new PaymentsWithinRangeSpecification(from, to), cancellationToken);
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // Index eShop payments by every PayPal id they own, and by their unique invoice id. Both keys
        // are globally unique, so matching never produces a false positive from an unrelated order that
        // happens to reuse a database id.
        var paymentByPayPalId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        var paymentByInvoiceId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            AddKey(paymentByInvoiceId, payment.InvoiceId, payment);
            AddKey(paymentByPayPalId, payment.PayPalOrderId, payment);
            AddKey(paymentByPayPalId, payment.AuthorizationId, payment);
            AddKey(paymentByPayPalId, payment.CaptureId, payment);
            foreach (var refund in payment.Refunds)
            {
                AddKey(paymentByPayPalId, refund.PayPalRefundId, payment);
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentOrderIds = new HashSet<int>();
        var matchedCount = 0;
        var missingInEShopCount = 0;

        foreach (var txn in transactions)
        {
            var payment = ResolvePayment(txn, paymentByPayPalId, paymentByInvoiceId);
            if (payment is not null)
            {
                matchedCount++;
                matchedPaymentOrderIds.Add(payment.OrderId);
                entries.Add(BuildEntry("MATCHED", txn, payment));
            }
            else
            {
                missingInEShopCount++;
                entries.Add(BuildEntry("MISSING_IN_ESHOP", txn, null));
            }
        }

        // eShop payments PayPal has not reported yet (expected in sandbox: reporting lags live activity).
        var missingInPayPal = payments.Where(p => !matchedPaymentOrderIds.Contains(p.OrderId)).ToList();
        foreach (var payment in missingInPayPal)
        {
            entries.Add(new ReconciliationEntry(
                Status: "MISSING_IN_PAYPAL",
                PayPalTransactionId: null,
                EventCode: null,
                PayPalStatus: null,
                PayPalAmount: null,
                CurrencyCode: payment.CurrencyCode,
                PayPalDate: null,
                OrderId: payment.OrderId,
                PayPalOrderId: payment.PayPalOrderId,
                EShopAmount: payment.CapturedGrossAmount ?? payment.Amount,
                EShopPaymentStatus: payment.Status.ToString()));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {transactions.Count} PayPal txns, {payments.Count} eShop payments, " +
            $"{matchedCount} matched, {missingInEShopCount} only in PayPal, {missingInPayPal.Count} only in eShop.");

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            EShopPaymentCount: payments.Count,
            MatchedCount: matchedCount,
            MissingInEShopCount: missingInEShopCount,
            MissingInPayPalCount: missingInPayPal.Count,
            Entries: entries);
    }

    private static void AddKey(IDictionary<string, Payment> map, string? key, Payment payment)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            map[key!] = payment;
        }
    }

    private static Payment? ResolvePayment(
        PayPalTransaction txn,
        IReadOnlyDictionary<string, Payment> byPayPalId,
        IReadOnlyDictionary<string, Payment> byInvoiceId)
    {
        // The transaction id equals the capture/refund id for those transactions; the base/reference
        // id links back to the PayPal order/authorization. Match on any id this payment owns.
        if (!string.IsNullOrWhiteSpace(txn.TransactionId) && byPayPalId.TryGetValue(txn.TransactionId, out var p1))
        {
            return p1;
        }

        // The invoice id is globally unique per payment (it carries a timestamp and a GUID).
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId) && byInvoiceId.TryGetValue(txn.InvoiceId!.Trim(), out var p2))
        {
            return p2;
        }

        return null;
    }

    private static ReconciliationEntry BuildEntry(string status, PayPalTransaction txn, Payment? payment) =>
        new(
            Status: status,
            PayPalTransactionId: txn.TransactionId,
            EventCode: txn.EventCode,
            PayPalStatus: txn.Status,
            PayPalAmount: txn.GrossAmount,
            CurrencyCode: txn.CurrencyCode ?? payment?.CurrencyCode,
            PayPalDate: txn.InitiationDate,
            OrderId: payment?.OrderId,
            PayPalOrderId: payment?.PayPalOrderId,
            EShopAmount: payment?.CapturedGrossAmount ?? payment?.Amount,
            EShopPaymentStatus: payment?.Status.ToString());
}
