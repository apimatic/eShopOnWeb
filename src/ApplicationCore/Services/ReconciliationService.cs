using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalPaymentService payPal, IReadRepository<Payment> paymentRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new PaymentFlowException("'to' must be on or after 'from'.");
        }

        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(ct);

        var paymentsByInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceReference))
            .GroupBy(p => p.InvoiceReference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lines = new List<ReconciliationLine>();
        var matchedInvoices = new HashSet<string>(StringComparer.Ordinal);

        // Walk every PayPal transaction: matched against an eShop payment, or PayPal-only.
        foreach (var txn in transactions)
        {
            Payment? payment = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId))
            {
                paymentsByInvoice.TryGetValue(txn.InvoiceId!, out payment);
            }

            if (payment is not null)
            {
                matchedInvoices.Add(payment.InvoiceReference);
                lines.Add(new ReconciliationLine("Matched", payment.InvoiceReference, payment.OrderId,
                    txn.TransactionId, txn.Status, txn.Amount, payment.Status.ToString(), payment.Amount));
            }
            else
            {
                lines.Add(new ReconciliationLine("PayPalOnly", txn.InvoiceId, null, txn.TransactionId, txn.Status,
                    txn.Amount, null, null));
            }
        }

        // eShop payments that reached PayPal (were authorized/captured) in the window but have no matching
        // PayPal transaction — the reverse discrepancy. (May legitimately be empty-or-populated given
        // PayPal's reporting lag.)
        foreach (var payment in payments)
        {
            var reachedPayPal = !string.IsNullOrEmpty(payment.PayPalOrderId);
            var inWindow = payment.CreatedDate >= from && payment.CreatedDate <= to;
            if (reachedPayPal && inWindow && !matchedInvoices.Contains(payment.InvoiceReference))
            {
                lines.Add(new ReconciliationLine("EShopOnly", payment.InvoiceReference, payment.OrderId, null, null,
                    null, payment.Status.ToString(), payment.Amount));
            }
        }

        return new ReconciliationReport(
            from,
            to,
            transactions.Count,
            lines.Count(l => l.Disposition == "Matched"),
            lines.Count(l => l.Disposition == "PayPalOnly"),
            lines.Count(l => l.Disposition == "EShopOnly"),
            lines);
    }
}
