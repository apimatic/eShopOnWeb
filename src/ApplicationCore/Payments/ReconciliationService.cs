using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record ReconciliationEntry(
    string TransactionId,
    string Kind,
    int? OrderId,
    DateTimeOffset? OccurredAt,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    decimal? PayPalFee,
    string? PayPalStatus,
    string? PayPalEventCode,
    string? EShopStatus,
    bool? AmountsMatch,
    string? Note);

public sealed record ReconciliationWindow(DateTimeOffset From, DateTimeOffset To, int PagesFetched, int TotalPages, bool Complete);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool IsComplete,
    string? IncompleteReason,
    IReadOnlyList<ReconciliationWindow> Windows,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> PayPalOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly,
    string Note);

/// <summary>
/// Lines PayPal's own transaction record up against eShop's payments for a date range. The range is
/// split into windows PayPal accepts and every page of every window is read; if anything stops the
/// walk early the report says so (<see cref="ReconciliationReport.IsComplete"/>).
/// </summary>
public class ReconciliationService
{
    /// <summary>Backstop against a provider that never stops handing out pages.</summary>
    public const int MaxPagesPerWindow = 200;

    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Payment> _payments;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<Payment> payments)
    {
        _gateway = gateway;
        _payments = payments;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new PaymentValidationException("'to' must be later than 'from'.");

        var windows = new List<ReconciliationWindow>();
        var transactions = new Dictionary<string, ProviderTransaction>(StringComparer.Ordinal);
        string? incompleteReason = null;

        for (var windowStart = from; windowStart < to && incompleteReason is null;)
        {
            var windowEnd = windowStart + _gateway.MaxSearchRange < to ? windowStart + _gateway.MaxSearchRange : to;
            var page = 1;
            var totalPages = 0;
            var fetched = 0;
            while (true)
            {
                ProviderTransactionPage result;
                try
                {
                    result = await _gateway.SearchTransactionsAsync(windowStart, windowEnd, page, cancellationToken);
                }
                catch (PaymentProviderException ex) when (ex.Kind is PaymentProviderErrorKind.Timeout or PaymentProviderErrorKind.OutcomeUnknown)
                {
                    incompleteReason = $"PayPal did not respond in time while reading {windowStart:O} – {windowEnd:O} (page {page}); later transactions are not included.";
                    break;
                }

                fetched++;
                totalPages = result.TotalPages;
                foreach (var tx in result.Transactions)
                {
                    transactions.TryAdd(tx.TransactionId, tx);
                }

                if (page >= result.TotalPages || result.Transactions.Count == 0)
                    break;
                if (fetched >= MaxPagesPerWindow)
                {
                    incompleteReason = $"Stopped after {MaxPagesPerWindow} pages for {windowStart:O} – {windowEnd:O} of {result.TotalPages}; narrow the date range.";
                    break;
                }
                page++;
            }

            windows.Add(new ReconciliationWindow(windowStart, windowEnd, fetched, totalPages, incompleteReason is null));
            windowStart = windowEnd;
        }

        var payments = await _payments.ListAsync(new PaymentsCapturedBeforeSpec(to), cancellationToken);
        var expected = ExpectedTransactions(payments, from, to);
        var byInvoice = payments.GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var paypalOnly = new List<ReconciliationEntry>();
        foreach (var tx in transactions.Values.OrderBy(t => t.InitiatedAt))
        {
            if (expected.Remove(tx.TransactionId, out var known))
            {
                var amountsMatch = tx.Amount is { } paid ? Math.Abs(paid) == known.Amount : (bool?)null;
                matched.Add(new ReconciliationEntry(tx.TransactionId, known.Kind, known.Payment.OrderId, tx.InitiatedAt ?? known.At,
                    tx.Amount, known.Amount, tx.Currency ?? known.Payment.Currency, tx.Fee, tx.Status, tx.EventCode,
                    known.Status, amountsMatch, amountsMatch == false ? "Amounts differ." : null));
                continue;
            }

            int? relatedOrder = tx.InvoiceId is not null && byInvoice.TryGetValue(tx.InvoiceId, out var related) ? related.OrderId : null;
            paypalOnly.Add(new ReconciliationEntry(tx.TransactionId, "unknown", relatedOrder, tx.InitiatedAt,
                tx.Amount, null, tx.Currency, tx.Fee, tx.Status, tx.EventCode, null, null,
                relatedOrder is null
                    ? "PayPal recorded this transaction; eShop has no matching payment."
                    : $"Invoice matches eShop order {relatedOrder}, but eShop has no record of this transaction id."));
        }

        var eshopOnly = expected.Values
            .OrderBy(e => e.At)
            .Select(e => new ReconciliationEntry(e.TransactionId, e.Kind, e.Payment.OrderId, e.At,
                null, e.Amount, e.Payment.Currency, null, null, null, e.Status, null,
                "eShop recorded this transaction; it is not (yet) in PayPal's transaction record for the range."))
            .ToList();

        return new ReconciliationReport(from, to, incompleteReason is null, incompleteReason, windows, matched, paypalOnly, eshopOnly,
            "PayPal's transaction reporting can lag live activity by up to three hours; very recent transactions may show as eShop-only.");
    }

    private sealed record ExpectedTransaction(string TransactionId, string Kind, Payment Payment, decimal Amount, DateTimeOffset At, string Status);

    private static Dictionary<string, ExpectedTransaction> ExpectedTransactions(IEnumerable<Payment> payments, DateTimeOffset from, DateTimeOffset to)
    {
        var expected = new Dictionary<string, ExpectedTransaction>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && payment.CapturedAt is { } capturedAt && capturedAt >= from && capturedAt <= to)
            {
                expected[payment.CaptureId] = new ExpectedTransaction(payment.CaptureId, "capture", payment,
                    payment.CapturedAmount ?? payment.Amount, capturedAt, payment.CaptureStatus ?? payment.Status.ToString());
            }

            foreach (var refund in payment.Refunds)
            {
                var at = refund.CompletedAt ?? refund.RequestedAt;
                if (refund.ProviderRefundId is not null && refund.Status != PaymentRefundStatus.Failed && at >= from && at <= to)
                {
                    expected[refund.ProviderRefundId] = new ExpectedTransaction(refund.ProviderRefundId, "refund", payment,
                        refund.Amount, at, refund.ProviderStatus ?? refund.Status.ToString());
                }
            }
        }
        return expected;
    }
}
