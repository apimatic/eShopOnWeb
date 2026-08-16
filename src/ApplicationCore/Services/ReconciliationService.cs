using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("The 'to' date must not be earlier than the 'from' date.");

        // PayPal's own record for the whole range (all pages), and eShop's own records.
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // eShop money-movement records, keyed by the PayPal id we expect to see on the other side.
        var eShopByTxnId = new Dictionary<string, EShopRecord>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
                continue;

            if (payment.IsCaptured && payment.CaptureId is not null)
            {
                eShopByTxnId[payment.CaptureId] = new EShopRecord(
                    order.Id, "capture", payment.Status.ToString(), payment.CapturedAmount, payment.Currency);
            }

            foreach (var refund in payment.Refunds.Where(r => r.PayPalRefundId is not null))
            {
                eShopByTxnId[refund.PayPalRefundId!] = new EShopRecord(
                    order.Id, "refund", refund.Status.ToString(), refund.Amount, payment.Currency);
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matchedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var txn in payPalTransactions)
        {
            if (eShopByTxnId.TryGetValue(txn.TransactionId, out var record))
            {
                matchedIds.Add(txn.TransactionId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.Matched, txn.TransactionId, txn.Status, txn.Amount,
                    txn.CurrencyCode ?? record.CurrencyCode, record.OrderId, record.Reference, record.PaymentStatus));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationOutcome.InPayPalOnly, txn.TransactionId, txn.Status, txn.Amount,
                    txn.CurrencyCode, null, null, null));
            }
        }

        foreach (var (txnId, record) in eShopByTxnId)
        {
            if (matchedIds.Contains(txnId))
                continue;

            entries.Add(new ReconciliationEntry(
                ReconciliationOutcome.InEShopOnly, txnId, null, record.Amount,
                record.CurrencyCode, record.OrderId, record.Reference, record.PaymentStatus));
        }

        return new ReconciliationReport(from, to, entries);
    }

    private sealed record EShopRecord(int OrderId, string Reference, string PaymentStatus, decimal? Amount, string CurrencyCode);
}
