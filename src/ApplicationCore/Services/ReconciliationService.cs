using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    /// <summary>An eShop record of money that actually moved (a capture or a refund).</summary>
    private sealed record EShopRecord(
        string Kind, string PayPalId, string InvoiceId, decimal Amount, int OrderId,
        string Status, DateTimeOffset? Timestamp);

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // 1) What PayPal's own report says happened over the whole range.
        var payPalTransactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);

        // 2) What eShop's own records say moved money.
        var orders = await _orderRepository.ListAsync(new AllOrdersWithPaymentsSpecification(), cancellationToken);
        var eShopRecords = BuildEShopRecords(orders);

        var byPayPalId = eShopRecords
            .Where(r => !string.IsNullOrEmpty(r.PayPalId))
            .GroupBy(r => r.PayPalId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var captureByInvoice = eShopRecords
            .Where(r => r.Kind == "capture" && !string.IsNullOrEmpty(r.InvoiceId))
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationLine>();
        var missingInEShop = new List<ReconciliationLine>();
        var matchedEShopKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in payPalTransactions)
        {
            EShopRecord? record = null;
            if (byPayPalId.TryGetValue(txn.TransactionId, out var byId))
            {
                record = byId;
            }
            else if (!string.IsNullOrEmpty(txn.InvoiceId) && captureByInvoice.TryGetValue(txn.InvoiceId, out var byInv))
            {
                record = byInv;
            }
            else if (!string.IsNullOrEmpty(txn.CustomField) && captureByInvoice.TryGetValue(txn.CustomField!, out var byCustom))
            {
                record = byCustom;
            }

            if (record is not null)
            {
                matchedEShopKeys.Add(record.PayPalId);
                matched.Add(new ReconciliationLine(
                    ReconciliationOutcome.Matched,
                    txn.TransactionId, txn.Status, txn.Amount, txn.Currency,
                    record.InvoiceId, record.OrderId, record.Status, record.Amount,
                    $"{record.Kind} matched to PayPal transaction {txn.TransactionId}"));
            }
            else
            {
                missingInEShop.Add(new ReconciliationLine(
                    ReconciliationOutcome.MissingInEShop,
                    txn.TransactionId, txn.Status, txn.Amount, txn.Currency,
                    txn.InvoiceId, null, null, null,
                    "PayPal reports this transaction but eShop has no matching record."));
            }
        }

        // 3) eShop money movements in range that PayPal's report does not (yet) show.
        var missingInPayPal = new List<ReconciliationLine>();
        foreach (var record in eShopRecords)
        {
            if (matchedEShopKeys.Contains(record.PayPalId)) continue;
            if (record.Timestamp is null || record.Timestamp < from || record.Timestamp > to) continue;

            missingInPayPal.Add(new ReconciliationLine(
                ReconciliationOutcome.MissingInPayPal,
                record.PayPalId, null, null, _gateway.Currency,
                record.InvoiceId, record.OrderId, record.Status, record.Amount,
                $"eShop recorded a {record.Kind} that PayPal's report does not list " +
                "(PayPal transaction reporting can lag recent activity by up to a few hours)."));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: payPalTransactions.Count,
            EShopRecordCount: eShopRecords.Count,
            Matched: matched,
            MissingInEShop: missingInEShop,
            MissingInPayPal: missingInPayPal);
    }

    private static List<EShopRecord> BuildEShopRecords(IEnumerable<Order> orders)
    {
        var records = new List<EShopRecord>();
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;

            if (!string.IsNullOrEmpty(payment.CaptureId) && payment.CapturedAmount is decimal captured)
            {
                records.Add(new EShopRecord(
                    "capture", payment.CaptureId!, payment.InvoiceId, captured, order.Id,
                    payment.Status.ToString(), payment.CapturedAt));
            }

            foreach (var refund in payment.Refunds)
            {
                records.Add(new EShopRecord(
                    "refund", refund.PayPalRefundId, payment.InvoiceId, refund.Amount, order.Id,
                    refund.Status, refund.RefundedAt));
            }
        }

        return records;
    }
}
