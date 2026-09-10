using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction record up against eShop payments over a date range, so a payment
/// one side knows about and the other does not becomes visible. The gateway returns the whole range
/// (all pages); matching is by invoice id, then by PayPal transaction id against the capture/auth id.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _gateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalGateway gateway, IReadRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop payments that had PayPal activity and fall in the window (in-memory store is small).
        var allPayments = await _paymentRepository.ListAsync(ct);
        var expected = allPayments
            .Where(p => p.PayPalOrderId is not null && p.CreatedAt >= from && p.CreatedAt <= to)
            .ToList();

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = FindMatch(txn, expected);
            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.Matched,
                    txn.TransactionId, txn.InvoiceId ?? match.InvoiceId, txn.Amount, txn.Status, txn.InitiationDate,
                    match.OrderId, match.Amount, match.Status.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.PayPalOnly,
                    txn.TransactionId, txn.InvoiceId, txn.Amount, txn.Status, txn.InitiationDate,
                    null, null, null));
            }
        }

        foreach (var payment in expected.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.EShopOnly,
                payment.CaptureId ?? payment.AuthorizationId, payment.InvoiceId, null, null, null,
                payment.OrderId, payment.Amount, payment.Status.ToString()));
        }

        return new ReconciliationReport(
            from, to,
            transactions.Count,
            entries.Count(e => e.Match == ReconciliationMatch.Matched),
            entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly),
            entries.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            entries);
    }

    private static Payment? FindMatch(PayPalTransaction txn, IEnumerable<Payment> expected)
    {
        return expected.FirstOrDefault(p =>
            (!string.IsNullOrEmpty(txn.InvoiceId) && string.Equals(txn.InvoiceId, p.InvoiceId, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(txn.TransactionId) &&
                (string.Equals(txn.TransactionId, p.CaptureId, StringComparison.Ordinal)
                 || string.Equals(txn.TransactionId, p.AuthorizationId, StringComparison.Ordinal))));
    }
}
