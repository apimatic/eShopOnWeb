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
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;

    public ReconciliationService(
        IReadRepository<Payment> paymentRepository,
        IPayPalClient payPalClient,
        PayPalSettings settings)
    {
        _paymentRepository = paymentRepository;
        _payPalClient = payPalClient;
        _settings = settings;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // PayPal's own record of transactions across the whole range (all pages, chunked to its 31-day window).
        var transactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);

        var capturedPayments = await _paymentRepository.ListAsync(new CapturedPaymentsSpecification(), cancellationToken);
        var paymentByInvoice = capturedPayments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop side of the range: payments captured within [from, to].
        var eShopInRange = capturedPayments
            .Where(p => p.CapturedAt.HasValue && p.CapturedAt.Value >= from && p.CapturedAt.Value <= to)
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<PayPalTransactionRecord>();
        var matchedInvoices = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            if (transaction.InvoiceId is not null && paymentByInvoice.TryGetValue(transaction.InvoiceId, out var payment))
            {
                matched.Add(new ReconciliationMatch(
                    payment.OrderId,
                    transaction.InvoiceId,
                    payment.CapturedAmount ?? 0m,
                    transaction.TransactionId,
                    transaction.Status,
                    transaction.Amount));
                matchedInvoices.Add(transaction.InvoiceId);
            }
            else
            {
                // A payment PayPal knows about that eShop can't line up to an order.
                inPayPalOnly.Add(transaction);
            }
        }

        // Captured eShop orders in range that PayPal's report does not (yet) show.
        var inEShopOnly = eShopInRange
            .Where(p => !matchedInvoices.Contains(p.InvoiceId))
            .Select(p => new ReconciliationEShopEntry(
                p.OrderId,
                p.InvoiceId,
                p.CapturedAmount ?? 0m,
                p.Status.ToString(),
                p.CapturedAt))
            .ToList();

        var summary = new ReconciliationSummary(
            transactions.Count,
            eShopInRange.Count,
            matched.Count,
            inPayPalOnly.Count,
            inEShopOnly.Count);

        return new ReconciliationReport(from, to, _settings.Currency, matched, inPayPalOnly, inEShopOnly, summary);
    }
}
