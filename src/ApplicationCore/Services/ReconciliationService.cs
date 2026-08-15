using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _paypal;
    private readonly IReadRepository<Payment> _payments;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalGateway paypal,
        IReadRepository<Payment> payments,
        IAppLogger<ReconciliationService> logger)
    {
        _paypal = paypal;
        _payments = payments;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            throw PaymentApiException.BadRequest("'to' must be on or after 'from'.");
        }

        // PayPal's own record over the WHOLE range (the gateway follows pagination to the last page).
        var transactions = await _paypal.SearchTransactionsAsync(from, to, ct);

        // eShop's own record: every capture and refund we believe PayPal should know about.
        var payments = await _payments.ListAsync(new AllPaymentsWithRefundsSpecification(), ct);
        var eShopRefs = BuildEShopReferences(payments);

        var payPalIds = new HashSet<string>(
            transactions.Where(t => !string.IsNullOrEmpty(t.TransactionId)).Select(t => t.TransactionId),
            StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();

        foreach (var txn in transactions)
        {
            var match = txn.TransactionId is not null ? eShopRefs.GetValueOrDefault(txn.TransactionId) : null;
            lines.Add(new ReconciliationLine(
                match is null ? ReconciliationState.MissingInEShop : ReconciliationState.Matched,
                txn.TransactionId,
                txn.Status,
                txn.Amount,
                txn.CurrencyCode,
                txn.InitiationDate,
                match?.OrderId,
                match?.Reference,
                match?.Kind));
        }

        foreach (var reference in eShopRefs.Values)
        {
            if (!payPalIds.Contains(reference.Reference))
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationState.MissingInPayPal,
                    null, null, reference.Amount, reference.CurrencyCode, null,
                    reference.OrderId, reference.Reference, reference.Kind));
            }
        }

        var matched = lines.Count(l => l.State == ReconciliationState.Matched);
        var missingInEShop = lines.Count(l => l.State == ReconciliationState.MissingInEShop);
        var missingInPayPal = lines.Count(l => l.State == ReconciliationState.MissingInPayPal);

        _logger.LogInformation("Reconciliation {0:o}..{1:o}: paypal={2} eshop={3} matched={4} missingInEShop={5} missingInPayPal={6}",
            from, to, transactions.Count, eShopRefs.Count, matched, missingInEShop, missingInPayPal);

        return new ReconciliationReport(from, to, transactions.Count, eShopRefs.Count,
            matched, missingInEShop, missingInPayPal, lines);
    }

    private static Dictionary<string, EShopReference> BuildEShopReferences(IEnumerable<Payment> payments)
    {
        var refs = new Dictionary<string, EShopReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null)
            {
                refs[payment.CaptureId] = new EShopReference(payment.OrderId, payment.CaptureId, "capture",
                    payment.CapturedAmount, payment.CurrencyCode);
            }
            foreach (var refund in payment.Refunds)
            {
                refs[refund.RefundId] = new EShopReference(payment.OrderId, refund.RefundId, "refund",
                    refund.Amount, payment.CurrencyCode);
            }
        }
        return refs;
    }

    private sealed record EShopReference(int OrderId, string Reference, string Kind, decimal? Amount, string CurrencyCode);
}
