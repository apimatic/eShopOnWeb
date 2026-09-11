using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's transaction record up against eShop's captured payments over a date range so that a
/// payment PayPal knows about but eShop doesn't (or the reverse) becomes visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<OrderPayment> paymentRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be on or after 'from'.");
        }

        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var localPayments = await _paymentRepository.ListAsync(new CapturedOrderPaymentsSpec(), cancellationToken);

        // Index local captured payments by their external reference and by capture id.
        var byInvoice = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        var byCaptureId = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in localPayments)
        {
            byInvoice[OrderPaymentService.InvoiceIdFor(p.OrderId)] = p;
            if (!string.IsNullOrEmpty(p.CaptureId))
            {
                byCaptureId[p.CaptureId!] = p;
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = ResolveLocal(txn, byInvoice, byCaptureId);
            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.Matched,
                    txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Fee, txn.CurrencyCode, txn.Date,
                    txn.InvoiceId ?? txn.CustomField, match.OrderId, match.CaptureId, match.CapturedAmount, match.Status.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.InPayPalOnly,
                    txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Fee, txn.CurrencyCode, txn.Date,
                    txn.InvoiceId ?? txn.CustomField, null, null, null, null));
            }
        }

        // eShop captures in the window that PayPal's report does not (yet) show.
        foreach (var p in localPayments)
        {
            if (matchedPaymentIds.Contains(p.Id))
            {
                continue;
            }
            if (p.UpdatedAt < from || p.UpdatedAt > to)
            {
                continue;
            }
            entries.Add(new ReconciliationEntry(
                ReconciliationOutcome.InEShopOnly,
                null, null, null, null, null, p.CurrencyCode, p.UpdatedAt,
                OrderPaymentService.InvoiceIdFor(p.OrderId), p.OrderId, p.CaptureId, p.CapturedAmount, p.Status.ToString()));
        }

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: entries.Count(e => e.Outcome == ReconciliationOutcome.Matched),
            InPayPalOnlyCount: entries.Count(e => e.Outcome == ReconciliationOutcome.InPayPalOnly),
            InEShopOnlyCount: entries.Count(e => e.Outcome == ReconciliationOutcome.InEShopOnly),
            entries);
    }

    private static OrderPayment? ResolveLocal(
        PayPalTransaction txn,
        IReadOnlyDictionary<string, OrderPayment> byInvoice,
        IReadOnlyDictionary<string, OrderPayment> byCaptureId)
    {
        foreach (var reference in new[] { txn.InvoiceId, txn.CustomField })
        {
            if (!string.IsNullOrEmpty(reference) && byInvoice.TryGetValue(reference!, out var byRef))
            {
                return byRef;
            }
        }

        if (!string.IsNullOrEmpty(txn.TransactionId) && byCaptureId.TryGetValue(txn.TransactionId, out var byCap))
        {
            return byCap;
        }

        return null;
    }
}
