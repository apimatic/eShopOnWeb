using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Result<ReconciliationReport>.Invalid(new ValidationError { ErrorMessage = "'to' must be on or after 'from'." });
        }

        // PayPal's own record over the whole range (paging/window-chunking handled by the gateway).
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop's own record: every capture and refund we performed, keyed by the id PayPal reports.
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpecification(), ct);
        var eShopReferences = BuildEShopReferences(payments);

        var payPalIds = new HashSet<string>(
            payPalTransactions.Where(t => t.TransactionId is not null).Select(t => t.TransactionId!),
            StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReconciliationRow>();

        // Every PayPal transaction: matched to an eShop record, or PayPal-only.
        foreach (var txn in payPalTransactions)
        {
            eShopReferences.TryGetValue(txn.TransactionId ?? string.Empty, out var reference);
            rows.Add(new ReconciliationRow(
                reference is null ? ReconciliationMatch.PayPalOnly : ReconciliationMatch.Matched,
                txn.TransactionId,
                txn.Status,
                txn.Amount,
                txn.CurrencyCode,
                txn.FeeAmount,
                txn.Date,
                reference?.OrderId,
                reference?.Kind,
                reference?.ReferenceId));
        }

        // eShop records PayPal did not return (accounting for reporting lag): eShop-only.
        foreach (var reference in eShopReferences.Values)
        {
            if (!payPalIds.Contains(reference.ReferenceId))
            {
                rows.Add(new ReconciliationRow(
                    ReconciliationMatch.EShopOnly,
                    null, null, null, null, null, null,
                    reference.OrderId, reference.Kind, reference.ReferenceId));
            }
        }

        var matched = rows.Count(r => r.Match == ReconciliationMatch.Matched);
        var payPalOnly = rows.Count(r => r.Match == ReconciliationMatch.PayPalOnly);
        var eShopOnly = rows.Count(r => r.Match == ReconciliationMatch.EShopOnly);

        var report = new ReconciliationReport(
            from, to, payPalTransactions.Count, matched, payPalOnly, eShopOnly, rows);
        return Result<ReconciliationReport>.Success(report);
    }

    private static Dictionary<string, EShopReference> BuildEShopReferences(IEnumerable<Payment> payments)
    {
        var map = new Dictionary<string, EShopReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                map[payment.CaptureId!] = new EShopReference(payment.OrderId, "capture", payment.CaptureId!);
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.RefundId))
                {
                    map[refund.RefundId] = new EShopReference(payment.OrderId, "refund", refund.RefundId);
                }
            }
        }
        return map;
    }

    private sealed record EShopReference(int OrderId, string Kind, string ReferenceId);
}
