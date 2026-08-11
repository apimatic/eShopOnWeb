using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop's payment records
/// (captures and refunds), so a payment PayPal knows about and eShop does not — or the reverse —
/// is visible. Reconciles the whole range: <see cref="IPaymentGateway.SearchTransactionsAsync"/>
/// already walks PayPal's 31-day window and page limits.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    private record EShopRecord(string TransactionId, int OrderId, string RecordType, decimal Amount);

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentValidationException("The reconciliation 'to' date must not be earlier than 'from'.");
        }

        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new PaidOrdersSpec(), cancellationToken);

        // eShop's money movements (captures + refunds) whose date falls inside the range.
        var eShopRecords = new List<EShopRecord>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (payment.CaptureId is not null && payment.CapturedGrossAmount is decimal gross
                && InRange(payment.CapturedAt, from, to))
            {
                eShopRecords.Add(new EShopRecord(payment.CaptureId, order.Id, "Capture", gross));
            }

            foreach (var refund in payment.Refunds)
            {
                if (InRange(refund.CreatedAt, from, to))
                {
                    eShopRecords.Add(new EShopRecord(refund.PayPalRefundId, order.Id, "Refund", refund.Amount));
                }
            }
        }

        var eShopById = eShopRecords
            .GroupBy(r => r.TransactionId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in payPalTransactions)
        {
            if (eShopById.TryGetValue(tx.TransactionId, out var record))
            {
                matchedIds.Add(tx.TransactionId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.Matched,
                    tx.TransactionId, tx.Status, tx.Amount, tx.Currency, tx.Date,
                    record.OrderId, record.RecordType, record.Amount));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.InPayPalOnly,
                    tx.TransactionId, tx.Status, tx.Amount, tx.Currency, tx.Date,
                    null, null, null));
            }
        }

        foreach (var record in eShopRecords)
        {
            if (matchedIds.Contains(record.TransactionId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                ReconciliationOutcome.InEShopOnly,
                record.TransactionId, null, null, _gateway.Currency, null,
                record.OrderId, record.RecordType, record.Amount));
        }

        return new ReconciliationReport(
            from,
            to,
            entries.Count(e => e.Outcome == ReconciliationOutcome.Matched),
            entries.Count(e => e.Outcome == ReconciliationOutcome.InPayPalOnly),
            entries.Count(e => e.Outcome == ReconciliationOutcome.InEShopOnly),
            entries);
    }

    private static bool InRange(DateTimeOffset? when, DateTimeOffset from, DateTimeOffset to) =>
        when.HasValue && when.Value >= from && when.Value <= to;
}
