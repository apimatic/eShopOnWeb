using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalClient payPal,
        IReadRepository<OrderPayment> paymentRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentFlowException("Reconciliation 'to' must be on or after 'from'.", 400);
        }

        // PayPal's own record across the whole range (chunked + fully paginated inside the client).
        var transactions = await _payPal.ListTransactionsAsync(from, to, ct);

        // eShop's own record of captured payments.
        var payments = await _paymentRepository.ListAsync(new CapturedPaymentsSpecification(), ct);
        var byCaptureId = payments
            .Where(p => p.CaptureId is not null)
            .ToDictionary(p => p.CaptureId!, StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedCaptureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in transactions)
        {
            var match = MatchToPayment(t, byCaptureId, payments);
            if (match is not null)
            {
                matchedCaptureIds.Add(match.CaptureId!);
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.Matched,
                    t.TransactionId,
                    match.OrderId,
                    match.CaptureId,
                    t.Amount,
                    match.CapturedAmount,
                    t.Currency,
                    t.Status,
                    t.Date));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.InPayPalOnly,
                    t.TransactionId,
                    null,
                    null,
                    t.Amount,
                    null,
                    t.Currency,
                    t.Status,
                    t.Date));
            }
        }

        // eShop captures within the window that PayPal's report does not (yet) show.
        foreach (var p in payments)
        {
            if (p.CaptureId is null || matchedCaptureIds.Contains(p.CaptureId))
            {
                continue;
            }
            var capturedAt = p.CapturedAt ?? p.UpdatedAt;
            if (capturedAt < from || capturedAt > to)
            {
                continue;
            }

            lines.Add(new ReconciliationLine(
                ReconciliationMatch.InEShopOnly,
                null,
                p.OrderId,
                p.CaptureId,
                null,
                p.CapturedAmount,
                p.Currency,
                p.Status.ToString(),
                capturedAt));
        }

        var report = new ReconciliationReport(
            from,
            to,
            transactions.Count,
            payments.Count,
            lines.Count(l => l.Match == ReconciliationMatch.Matched),
            lines.Count(l => l.Match == ReconciliationMatch.InPayPalOnly),
            lines.Count(l => l.Match == ReconciliationMatch.InEShopOnly),
            lines
                .OrderByDescending(l => l.TransactionDate ?? DateTimeOffset.MinValue)
                .ToList());

        _logger.LogInformation($"Reconciliation {from:o}..{to:o}: {report.PayPalTransactionCount} PayPal txns, " +
            $"{report.MatchedCount} matched, {report.InPayPalOnlyCount} PayPal-only, {report.InEShopOnlyCount} eShop-only.");
        return report;
    }

    private static OrderPayment? MatchToPayment(
        PayPalTransaction t,
        IReadOnlyDictionary<string, OrderPayment> byCaptureId,
        IReadOnlyList<OrderPayment> payments)
    {
        // A capture transaction's id equals the eShop capture id.
        if (byCaptureId.TryGetValue(t.TransactionId, out var direct))
        {
            return direct;
        }
        // Fall back to the merchant reference we stamped on the order (as both invoice_id and custom_id).
        if (!string.IsNullOrEmpty(t.InvoiceId))
        {
            var byInvoice = payments.FirstOrDefault(p => p.PaymentReference == t.InvoiceId);
            if (byInvoice is not null)
            {
                return byInvoice;
            }
        }
        if (!string.IsNullOrEmpty(t.CustomField))
        {
            var byCustom = payments.FirstOrDefault(p => p.PaymentReference == t.CustomField);
            if (byCustom is not null)
            {
                return byCustom;
            }
        }
        return null;
    }
}
