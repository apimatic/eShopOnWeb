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
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Payment> paymentRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Whole range, following pagination — not just the first page.
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // eShop's side: every payment that actually reached PayPal (money moved) within the range.
        var payments = (await _paymentRepository.ListAsync(new AllPaymentsSpec(), ct))
            .Where(p => p.PayPalOrderId is not null && p.CreatedAt >= from && p.CreatedAt <= to)
            .ToList();

        // Match case-insensitively: PayPal's reporting can echo invoice ids in a different case.
        var paymentByInvoice = payments
            .GroupBy(InvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            if (txn.InvoiceId is not null && paymentByInvoice.TryGetValue(txn.InvoiceId, out var payment))
            {
                matchedInvoices.Add(txn.InvoiceId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationState.Matched, txn.TransactionId, txn.InvoiceId, txn.Amount, txn.Status,
                    txn.InitiationDate, payment.OrderId, payment.Amount, payment.Status.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationState.MissingInEShop, txn.TransactionId, txn.InvoiceId, txn.Amount,
                    txn.Status, txn.InitiationDate, null, null, null));
            }
        }

        foreach (var payment in payments)
        {
            var invoice = InvoiceId(payment);
            if (!matchedInvoices.Contains(invoice))
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationState.MissingInPayPal, null, invoice, null, null, null,
                    payment.OrderId, payment.Amount, payment.Status.ToString()));
            }
        }

        return new ReconciliationReport(
            from, to,
            transactions.Count,
            entries.Count(e => e.State == ReconciliationState.Matched),
            entries.Count(e => e.State == ReconciliationState.MissingInEShop),
            entries.Count(e => e.State == ReconciliationState.MissingInPayPal),
            entries);
    }

    // The invoice id stamped on the PayPal order when it was authorized.
    private static string InvoiceId(Payment payment) => payment.PayPalInvoiceId;
}
