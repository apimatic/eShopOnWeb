using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalClient payPal,
        IRepository<Payment> paymentRepository,
        PayPalSettings settings,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be on or after 'from'.");
        }

        // PayPal's own record across the whole range (chunked and fully paginated in the client).
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's settled payments (captures and their refunds).
        var payments = await _paymentRepository.ListAsync(new SettledPaymentsSpecification(), cancellationToken);

        // Index eShop-side identifiers so a PayPal transaction can be traced back to an order.
        var invoiceRefToOrder = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        var idToOrder = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            invoiceRefToOrder[p.InvoiceReference] = p;
            AddId(idToOrder, p.PayPalOrderId, p);
            AddId(idToOrder, p.AuthorizationId, p);
            AddId(idToOrder, p.CaptureId, p);
            foreach (var r in p.Refunds)
            {
                AddId(idToOrder, r.PayPalRefundId, p);
            }
        }

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotInEShop = new List<PayPalTransaction>();
        var payPalIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in payPalTransactions)
        {
            CollectIdentifiers(payPalIdentifiers, t);

            var owner = ResolveOwner(t, invoiceRefToOrder, idToOrder);
            if (owner is not null)
            {
                matched.Add(new ReconciliationMatch(
                    owner.OrderId, owner.Status.ToString(), t.TransactionId, t.EventCode,
                    t.Amount, t.CurrencyCode, t.Date));
            }
            else
            {
                inPayPalNotInEShop.Add(t);
            }
        }

        // eShop settlement events in range that PayPal's report does not (yet) show.
        var inEShopNotInPayPal = new List<ReconciliationEShopOnlyEntry>();
        foreach (var p in payments)
        {
            if (p.CaptureId is not null && p.CapturedAt is { } capturedAt && InRange(capturedAt, from, to))
            {
                if (!IsKnownToPayPal(p, p.CaptureId, payPalIdentifiers))
                {
                    inEShopNotInPayPal.Add(new ReconciliationEShopOnlyEntry(
                        p.OrderId, "capture", p.CaptureId, p.CapturedAmount ?? p.Amount, p.Status.ToString()));
                }
            }

            foreach (var r in p.Refunds.Where(r => r.IsCompleted && r.PayPalRefundId is not null))
            {
                if (!InRange(r.CreatedAt, from, to)) continue;
                if (!IsKnownToPayPal(p, r.PayPalRefundId!, payPalIdentifiers))
                {
                    inEShopNotInPayPal.Add(new ReconciliationEShopOnlyEntry(
                        p.OrderId, "refund", r.PayPalRefundId!, r.Amount, p.Status.ToString()));
                }
            }
        }

        _logger.LogInformation(
            "Reconciliation {0:o}..{1:o}: {2} PayPal txns, {3} matched, {4} PayPal-only, {5} eShop-only.",
            from, to, payPalTransactions.Count, matched.Count, inPayPalNotInEShop.Count, inEShopNotInPayPal.Count);

        return new ReconciliationReport(
            from, to, _settings.Currency, payPalTransactions.Count,
            matched, inPayPalNotInEShop, inEShopNotInPayPal);
    }

    private static void AddId(IDictionary<string, Payment> map, string? id, Payment payment)
    {
        if (!string.IsNullOrWhiteSpace(id)) map[id!] = payment;
    }

    private static void CollectIdentifiers(ISet<string> set, PayPalTransaction t)
    {
        if (!string.IsNullOrWhiteSpace(t.TransactionId)) set.Add(t.TransactionId);
        if (!string.IsNullOrWhiteSpace(t.ReferenceId)) set.Add(t.ReferenceId!);
        if (!string.IsNullOrWhiteSpace(t.InvoiceId)) set.Add(t.InvoiceId!);
    }

    private static Payment? ResolveOwner(
        PayPalTransaction t,
        IReadOnlyDictionary<string, Payment> invoiceRefToOrder,
        IReadOnlyDictionary<string, Payment> idToOrder)
    {
        if (!string.IsNullOrWhiteSpace(t.InvoiceId) && invoiceRefToOrder.TryGetValue(t.InvoiceId!, out var byInvoice))
            return byInvoice;
        if (!string.IsNullOrWhiteSpace(t.TransactionId) && idToOrder.TryGetValue(t.TransactionId, out var byTxn))
            return byTxn;
        if (!string.IsNullOrWhiteSpace(t.ReferenceId) && idToOrder.TryGetValue(t.ReferenceId!, out var byRef))
            return byRef;
        return null;
    }

    private static bool IsKnownToPayPal(Payment payment, string settlementId, ISet<string> payPalIdentifiers)
        => payPalIdentifiers.Contains(settlementId) || payPalIdentifiers.Contains(payment.InvoiceReference);

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to)
        => value >= from && value <= to;
}
