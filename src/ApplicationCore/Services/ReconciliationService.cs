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
    private readonly IReadRepository<Payment> _payments;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalClient payPal,
        IReadRepository<Payment> payments,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _payments = payments;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.");

        // PayPal's full ledger over the range (paged + windowed inside the client).
        var transactions = await _payPal.ListTransactionsAsync(from, to, ct);
        var payments = await _payments.ListAsync(new AllPaymentsWithRefundsSpec(), ct);

        // Index every PayPal id eShop knows about → the eShop record it belongs to.
        var eShopByPayPalId = new Dictionary<string, (int OrderId, string Kind, string? Status)>(StringComparer.OrdinalIgnoreCase);
        // Namespaced custom-id token → order id, so a custom_field/invoice_id lines up exactly (no false hits).
        var tokenToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            tokenToOrder[PaymentCorrelation.OrderToken(p.OrderId)] = p.OrderId;
            if (!string.IsNullOrEmpty(p.PayPalOrderId)) eShopByPayPalId[p.PayPalOrderId!] = (p.OrderId, "order", p.Status.ToString());
            if (!string.IsNullOrEmpty(p.AuthorizationId)) eShopByPayPalId[p.AuthorizationId!] = (p.OrderId, "authorization", p.AuthorizationStatus);
            if (!string.IsNullOrEmpty(p.CaptureId)) eShopByPayPalId[p.CaptureId!] = (p.OrderId, "capture", p.CaptureStatus);
            foreach (var r in p.Refunds)
                eShopByPayPalId[r.PayPalRefundId] = (p.OrderId, "refund", r.Status);
        }

        var matched = new List<ReconciliationEntry>();
        var inPayPalNotEShop = new List<ReconciliationEntry>();
        var seenPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in transactions)
        {
            if (!string.IsNullOrEmpty(t.TransactionId)) seenPayPalIds.Add(t.TransactionId);

            (int OrderId, string Kind, string? Status)? hit = null;
            if (!string.IsNullOrEmpty(t.TransactionId) && eShopByPayPalId.TryGetValue(t.TransactionId, out var byId))
                hit = byId;
            else if (!string.IsNullOrEmpty(t.CustomField) && tokenToOrder.TryGetValue(t.CustomField!, out var oid))
                hit = (oid, "order", null);
            else if (!string.IsNullOrEmpty(t.InvoiceId) && tokenToOrder.TryGetValue(t.InvoiceId!, out var iid))
                hit = (iid, "order", null);

            var entry = new ReconciliationEntry(
                t.TransactionId, t.EventCode, t.Status, t.Amount, t.Currency, t.Date,
                hit?.OrderId, hit?.Kind, hit?.Status);

            if (hit is not null) matched.Add(entry);
            else inPayPalNotEShop.Add(entry);
        }

        // eShop settlement events (captures/refunds) in the range that PayPal has not reported yet.
        var inEShopNotPayPal = new List<ReconciliationEntry>();
        foreach (var p in payments)
        {
            var capturePending = !string.IsNullOrEmpty(p.CaptureId)
                && !seenPayPalIds.Contains(p.CaptureId!)
                && (InRange(p.UpdatedAt, from, to) || InRange(p.CreatedAt, from, to));
            if (capturePending)
            {
                inEShopNotPayPal.Add(new ReconciliationEntry(
                    p.CaptureId, null, p.CaptureStatus, p.CapturedAmount, p.Currency, p.UpdatedAt,
                    p.OrderId, "capture", p.CaptureStatus));
            }

            foreach (var r in p.Refunds)
            {
                if (!seenPayPalIds.Contains(r.PayPalRefundId) && InRange(r.CreatedAt, from, to))
                {
                    inEShopNotPayPal.Add(new ReconciliationEntry(
                        r.PayPalRefundId, null, r.Status, r.Amount, r.Currency, r.CreatedAt,
                        p.OrderId, "refund", r.Status));
                }
            }
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {transactions.Count} PayPal txns, {matched.Count} matched, " +
            $"{inPayPalNotEShop.Count} only-in-PayPal, {inEShopNotPayPal.Count} only-in-eShop.");

        return new ReconciliationReport(from, to, transactions.Count, matched.Count,
            matched, inPayPalNotEShop, inEShopNotPayPal);
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}
