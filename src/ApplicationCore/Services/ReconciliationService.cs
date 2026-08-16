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
    // eShop payments carry this prefix as their PayPal invoice_id / custom_id, so the report can
    // scope PayPal's records to those that belong to this application.
    private const string ReferencePrefix = "ESHOP-";

    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<OrderPayment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.");
        }

        // PayPal's own record over the whole range (the gateway pages through every result).
        var allTransactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);

        // Only transactions carrying an eShop reference are in scope for order-level reconciliation.
        var inScope = allTransactions
            .Where(t => StartsWithReference(t.InvoiceId) || StartsWithReference(t.CustomField))
            .ToList();

        // eShop's own record: payments captured within the range.
        var payments = await _paymentRepository.ListAsync(new CapturedOrderPaymentsInRangeSpecification(from, to), cancellationToken);
        var paymentsByReference = payments
            .GroupBy(p => p.ReconciliationReference)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var txn in inScope)
        {
            var reference = FirstReference(txn.InvoiceId, txn.CustomField);
            if (reference is not null && paymentsByReference.TryGetValue(reference, out var payment))
            {
                matchedReferences.Add(reference);
                lines.Add(new ReconciliationLine(
                    ReconciliationStatus.Matched,
                    reference,
                    payment.OrderId,
                    txn.TransactionId,
                    txn.Amount,
                    payment.Amount,
                    txn.CurrencyCode,
                    txn.Date));
            }
            else
            {
                // PayPal knows about this eShop-tagged transaction, but eShop has no matching order.
                lines.Add(new ReconciliationLine(
                    ReconciliationStatus.MissingInEShop,
                    reference,
                    null,
                    txn.TransactionId,
                    txn.Amount,
                    null,
                    txn.CurrencyCode,
                    txn.Date));
            }
        }

        // eShop captured payments that no PayPal transaction referenced (e.g. reporting lag or a lost capture).
        foreach (var payment in payments)
        {
            if (!matchedReferences.Contains(payment.ReconciliationReference))
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationStatus.MissingInPayPal,
                    payment.ReconciliationReference,
                    payment.OrderId,
                    payment.CaptureId,
                    null,
                    payment.CapturedGrossAmount ?? payment.Amount,
                    payment.CurrencyCode,
                    payment.CapturedAt));
            }
        }

        var matched = lines.Count(l => l.Status == ReconciliationStatus.Matched);
        var missingInEShop = lines.Count(l => l.Status == ReconciliationStatus.MissingInEShop);
        var missingInPayPal = lines.Count(l => l.Status == ReconciliationStatus.MissingInPayPal);

        return new ReconciliationReport(from, to, allTransactions.Count, matched, missingInEShop, missingInPayPal, lines);
    }

    private static bool StartsWithReference(string? value) =>
        value is not null && value.StartsWith(ReferencePrefix, StringComparison.Ordinal);

    private static string? FirstReference(params string?[] candidates) =>
        candidates.FirstOrDefault(StartsWithReference);
}
