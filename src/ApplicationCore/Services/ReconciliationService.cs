using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Cross-references PayPal's balance-affecting transactions (captures, refunds) for a date range
/// against eShop's own payment records. A transaction PayPal knows about but eShop doesn't — or the
/// reverse — becomes a visible line. Covers the whole range: the gateway follows PayPal's pagination.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var payPalTransactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpecification(), cancellationToken);

        // Map every eShop balance-affecting id (capture + refunds) created in the range to its order.
        var eShopById = new Dictionary<string, (int OrderId, string Kind)>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.CaptureId) && InRange(payment.UpdatedAt, from, to))
            {
                eShopById[payment.CaptureId!] = (payment.OrderId, "capture");
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.PayPalRefundId) && InRange(refund.CreatedAt, from, to))
                {
                    eShopById[refund.PayPalRefundId] = (payment.OrderId, "refund");
                }
            }
        }

        var lines = new List<ReconciliationLine>();
        var payPalIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var txn in payPalTransactions)
        {
            payPalIds.Add(txn.TransactionId);
            var matched = eShopById.TryGetValue(txn.TransactionId, out var eShop);
            lines.Add(new ReconciliationLine(
                txn.TransactionId,
                matched ? ReconciliationMatch.Matched : ReconciliationMatch.PayPalOnly,
                txn.Status,
                txn.Amount,
                txn.Currency,
                txn.Date,
                matched ? eShop.OrderId : null,
                matched ? eShop.Kind : txn.EventCode));
        }

        // eShop records PayPal's report did not return (possibly reporting lag in sandbox).
        foreach (var kvp in eShopById.Where(kvp => !payPalIds.Contains(kvp.Key)))
        {
            lines.Add(new ReconciliationLine(
                kvp.Key,
                ReconciliationMatch.EShopOnly,
                null, null, null, null,
                kvp.Value.OrderId,
                kvp.Value.Kind));
        }

        return new ReconciliationReport(
            from,
            to,
            payPalTransactions.Count,
            lines.Count(l => l.Match == ReconciliationMatch.Matched),
            lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly),
            lines.Count(l => l.Match == ReconciliationMatch.EShopOnly),
            lines);
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to)
        => value >= from && value <= to;
}
