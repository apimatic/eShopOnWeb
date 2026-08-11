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
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalGateway payPal,
        IReadRepository<Payment> paymentRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's own record for the whole range (the gateway pages and chunks as needed).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // The eShop side: payments carrying a PayPal invoice id created within the range.
        var payments = await _paymentRepository.ListAsync(new PaymentsForReconciliationSpecification(from, to), cancellationToken);
        var paymentsByInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedInvoices = new HashSet<string>();

        // One line per PayPal transaction: matched to an eShop order by invoice id, or PayPal-only.
        foreach (var txn in transactions)
        {
            Payment? payment = null;
            if (txn.InvoiceId is not null)
            {
                paymentsByInvoice.TryGetValue(txn.InvoiceId, out payment);
            }

            if (payment is not null)
            {
                matchedInvoices.Add(txn.InvoiceId!);
                entries.Add(new ReconciliationEntry(
                    ReconciliationStatus.Matched, txn.InvoiceId, txn.TransactionId, txn.EventCode,
                    txn.Amount, txn.FeeAmount, txn.CurrencyCode, txn.InitiationDate,
                    payment.OrderId, payment.Status.ToString(), payment.Amount));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationStatus.PayPalOnly, txn.InvoiceId, txn.TransactionId, txn.EventCode,
                    txn.Amount, txn.FeeAmount, txn.CurrencyCode, txn.InitiationDate,
                    null, null, null));
            }
        }

        // eShop payments PayPal's records (for this range) don't show.
        foreach (var payment in payments)
        {
            if (payment.InvoiceId is not null && !matchedInvoices.Contains(payment.InvoiceId))
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationStatus.EShopOnly, payment.InvoiceId, null, null,
                    null, null, payment.CurrencyCode, null,
                    payment.OrderId, payment.Status.ToString(), payment.Amount));
            }
        }

        var matchedCount = entries.Count(e => e.Status == ReconciliationStatus.Matched);
        var payPalOnlyCount = entries.Count(e => e.Status == ReconciliationStatus.PayPalOnly);
        var eShopOnlyCount = entries.Count(e => e.Status == ReconciliationStatus.EShopOnly);

        _logger.LogInformation("Reconciled {0}..{1}: {2} PayPal txns, {3} matched, {4} PayPal-only, {5} eShop-only.",
            from, to, transactions.Count, matchedCount, payPalOnlyCount, eShopOnlyCount);

        return new ReconciliationReport(from, to, transactions.Count, matchedCount, payPalOnlyCount, eShopOnlyCount, entries);
    }
}
