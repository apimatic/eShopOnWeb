using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record ReconciliationLine(
    string TransactionId,
    string? EventCode,
    string? Status,
    DateTimeOffset? Date,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    int? OrderId,
    string? MatchedAs,
    string? MatchedBy);

public record EShopOnlyLine(int OrderId, int PaymentId, string Kind, string Reference, decimal Amount, string Currency, DateTimeOffset? Date);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Complete,
    string? IncompleteReason,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> PayPalOnly,
    IReadOnlyList<EShopOnlyLine> EShopOnly,
    string Note);

/// <summary>
/// Lines PayPal's own transaction record for a date range up against eShop's payments, so a payment PayPal
/// knows about and eShop does not (or the reverse) is visible.
/// </summary>
public class ReconciliationService
{
    /// <summary>PayPal's transaction search accepts at most 31 days per request.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);
    public const int PageSize = 500;
    public const int MaxPagesPerWindow = 20;

    private readonly IPaymentGateway _gateway;
    private readonly IRepository<OrderPayment> _payments;

    public ReconciliationService(IPaymentGateway gateway, IRepository<OrderPayment> payments)
    {
        _gateway = gateway;
        _payments = payments;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from)
            throw new PaymentRequestException(PaymentErrorKind.Validation, "invalid_range", "'to' must be later than 'from'.");
        if (to - from > MaxRange)
            throw new PaymentRequestException(PaymentErrorKind.Validation, "range_too_large", $"The range may span at most {MaxRange.TotalDays:0} days.");

        var transactions = new List<GatewayTransaction>();
        var complete = true;
        string? incompleteReason = null;

        for (var windowStart = from; windowStart < to; windowStart += MaxWindow)
        {
            var windowEnd = windowStart + MaxWindow < to ? windowStart + MaxWindow : to;
            var page = 1;
            while (true)
            {
                var result = await _gateway.SearchTransactionsAsync(windowStart, windowEnd, page, PageSize, ct);
                transactions.AddRange(result.Transactions);
                if (page >= result.TotalPages || result.Transactions.Count == 0)
                    break;
                if (page >= MaxPagesPerWindow)
                {
                    complete = false;
                    incompleteReason = $"PayPal reported {result.TotalPages} pages for {windowStart:u} – {windowEnd:u}; only the first {MaxPagesPerWindow} " +
                                       $"({MaxPagesPerWindow * PageSize} transactions) were read. Narrow the date range.";
                    break;
                }
                page++;
            }
        }

        var payments = await _payments.ListAsync(new PaymentsWithProcessorActivitySpec(), ct);
        var byReference = new Dictionary<string, (OrderPayment Payment, string Kind)>(StringComparer.OrdinalIgnoreCase);
        var byInvoice = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            if (p.AuthorizationId is not null) byReference[p.AuthorizationId] = (p, "authorization");
            if (p.CaptureId is not null) byReference[p.CaptureId] = (p, "capture");
            foreach (var r in p.Refunds.Where(r => r.PayPalRefundId is not null))
                byReference[r.PayPalRefundId!] = (p, "refund");
            if (p.InvoiceId is not null) byInvoice[p.InvoiceId] = p;
        }

        var matched = new List<ReconciliationLine>();
        var payPalOnly = new List<ReconciliationLine>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transactions)
        {
            if (byReference.TryGetValue(t.TransactionId, out var hit))
            {
                seenReferences.Add(t.TransactionId);
                matched.Add(Line(t, hit.Payment.OrderId, hit.Kind, "transactionId"));
            }
            else if (t.InvoiceId is not null && byInvoice.TryGetValue(t.InvoiceId, out var byInv))
            {
                matched.Add(Line(t, byInv.OrderId, "payment", "invoiceId"));
            }
            else
            {
                payPalOnly.Add(Line(t, null, null, null));
            }
        }

        // eShop money movements in range that PayPal's record does not show.
        var eShopOnly = new List<EShopOnlyLine>();
        foreach (var p in payments)
        {
            if (p.CaptureId is not null && InRange(p.CapturedAt, from, to) && !seenReferences.Contains(p.CaptureId))
                eShopOnly.Add(new EShopOnlyLine(p.OrderId, p.Id, "capture", p.CaptureId, p.CapturedAmount ?? 0, p.Currency, p.CapturedAt));
            foreach (var r in p.Refunds.Where(r => r.Status == RefundStatus.Succeeded && r.PayPalRefundId is not null))
            {
                if (InRange(r.CompletedAt, from, to) && !seenReferences.Contains(r.PayPalRefundId!))
                    eShopOnly.Add(new EShopOnlyLine(p.OrderId, p.Id, "refund", r.PayPalRefundId!, r.Amount, p.Currency, r.CompletedAt));
            }
        }

        return new ReconciliationReport(from, to, complete, incompleteReason, transactions.Count, matched, payPalOnly, eShopOnly,
            "PayPal can take up to three hours to list executed transactions, so very recent eShop activity may appear under eShopOnly until it does.");
    }

    private static bool InRange(DateTimeOffset? at, DateTimeOffset from, DateTimeOffset to) => at is { } a && a >= from && a <= to;

    private static ReconciliationLine Line(GatewayTransaction t, int? orderId, string? kind, string? by) =>
        new(t.TransactionId, t.EventCode, t.Status, t.Date, t.Amount, t.Currency, t.Fee, t.InvoiceId, t.CustomField, orderId, kind, by);
}
