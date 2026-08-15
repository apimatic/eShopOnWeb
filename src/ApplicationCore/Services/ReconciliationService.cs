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

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IRepository<Payment> paymentRepository, IPayPalGateway payPal)
    {
        _paymentRepository = paymentRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new PaymentValidationException("'to' must be on or after 'from'.");

        // PayPal's own record across the WHOLE range (the gateway pages internally).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop payments created in the range, indexed by the tags we send to PayPal.
        var payments = await _paymentRepository.ListAsync(new PaymentsInRangeSpecification(from, to), cancellationToken);
        var byReference = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        var byInvoice = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            byReference[p.Reference.ToString("N")] = p;
            byInvoice[InvoiceIdFor(p)] = p;
        }

        var rows = new List<ReconciliationRow>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = ResolvePayment(txn, byReference, byInvoice);
            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
                rows.Add(new ReconciliationRow(
                    ReconciliationMatchState.Matched,
                    match.OrderId, match.Reference.ToString("N"), match.Status.ToString(), match.Amount,
                    txn.TransactionId, txn.Status, txn.Amount, txn.CurrencyCode, txn.Date));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    ReconciliationMatchState.PayPalOnly,
                    null, null, null, null,
                    txn.TransactionId, txn.Status, txn.Amount, txn.CurrencyCode, txn.Date));
            }
        }

        // eShop payments PayPal's report does not (yet) show — e.g. authorized-only or reporting lag.
        foreach (var p in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            rows.Add(new ReconciliationRow(
                ReconciliationMatchState.EShopOnly,
                p.OrderId, p.Reference.ToString("N"), p.Status.ToString(), p.Amount,
                null, null, null, p.CurrencyCode, null));
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Rows = rows,
            MatchedCount = rows.Count(r => r.State == ReconciliationMatchState.Matched),
            PayPalOnlyCount = rows.Count(r => r.State == ReconciliationMatchState.PayPalOnly),
            EShopOnlyCount = rows.Count(r => r.State == ReconciliationMatchState.EShopOnly)
        };
    }

    private static Payment? ResolvePayment(PayPalTransaction txn, IReadOnlyDictionary<string, Payment> byReference, IReadOnlyDictionary<string, Payment> byInvoice)
    {
        if (!string.IsNullOrEmpty(txn.CustomField) && byReference.TryGetValue(txn.CustomField!, out var byRef))
            return byRef;
        if (!string.IsNullOrEmpty(txn.InvoiceId) && byInvoice.TryGetValue(txn.InvoiceId!, out var byInv))
            return byInv;
        return null;
    }

    // Must match PaymentService.BuildInvoiceId exactly so PayPal transactions line up by invoice id.
    private static string InvoiceIdFor(Payment payment) => $"ESHOP-ORDER-{payment.OrderId}-{payment.Reference:N}";
}
