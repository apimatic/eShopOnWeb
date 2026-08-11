using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalReportingGateway _reporting;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalReportingGateway reporting, IReadRepository<Payment> paymentRepository)
    {
        _reporting = reporting;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _reporting.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpec(from, to), ct);

        // Index every id we hold locally, so a transaction can be matched by invoice, reference, or PayPal id.
        var localByPayPalRef = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            if (!string.IsNullOrEmpty(p.Reference)) localByPayPalRef[p.Reference] = p;
            foreach (var id in new[] { p.PayPalOrderId, p.AuthorizationId, p.CaptureId })
            {
                if (!string.IsNullOrEmpty(id)) localByPayPalRef[id!] = p;
            }
        }

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = MatchLocalPayment(txn, localByPayPalRef);
            if (match != null)
            {
                matchedOrderIds.Add(match.OrderId);
            }

            lines.Add(new ReconciliationLine(
                match != null ? ReconciliationSide.Matched : ReconciliationSide.PayPalOnly,
                txn.TransactionId,
                txn.Status,
                txn.EventCode,
                txn.InitiationDate,
                txn.Amount,
                txn.CurrencyCode,
                match?.OrderId,
                match?.Status,
                match?.Amount,
                match?.PayPalOrderId));
        }

        // eShop payments that authorized/captured but that PayPal's report does not (yet) show.
        foreach (var payment in payments)
        {
            if (matchedOrderIds.Contains(payment.OrderId)) continue;
            if (payment.Status == PaymentStatus.PendingPayment) continue; // nothing moved at PayPal yet

            lines.Add(new ReconciliationLine(
                ReconciliationSide.EShopOnly,
                null, null, null, payment.CreatedAt, null, payment.CurrencyCode,
                payment.OrderId, payment.Status, payment.Amount, payment.PayPalOrderId));
        }

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: transactions.Count,
            LocalPaymentCount: payments.Count,
            MatchedCount: lines.Count(l => l.Side == ReconciliationSide.Matched),
            PayPalOnlyCount: lines.Count(l => l.Side == ReconciliationSide.PayPalOnly),
            EShopOnlyCount: lines.Count(l => l.Side == ReconciliationSide.EShopOnly),
            Lines: lines);
    }

    private static Payment? MatchLocalPayment(ReconciliationTransaction txn,
        IReadOnlyDictionary<string, Payment> localByPayPalRef)
    {
        // Match on globally-unique keys only: the payment's unique reference (carried as invoice_id)
        // and PayPal's own ids (order/authorization/capture). custom_id carries the eShop order id
        // for human readability, but order ids are not globally unique across in-memory runs, so it
        // is deliberately not used as a join key.
        if (!string.IsNullOrEmpty(txn.InvoiceId) && localByPayPalRef.TryGetValue(txn.InvoiceId!, out var byInvoice))
        {
            return byInvoice;
        }

        if (!string.IsNullOrEmpty(txn.ReferenceId) && localByPayPalRef.TryGetValue(txn.ReferenceId!, out var byRef))
        {
            return byRef;
        }

        return null;
    }
}
