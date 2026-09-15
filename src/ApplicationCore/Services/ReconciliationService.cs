using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's own record of transactions across the whole range (already chunked + paginated).
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // Everything eShop knows: captures and refunds keyed by their PayPal id.
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var eShopByTransactionId = new Dictionary<string, ReconciliationEShopEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (payment.CaptureId is not null && WithinRange(order.OrderDate, from, to))
            {
                eShopByTransactionId[payment.CaptureId] = new ReconciliationEShopEntry(
                    order.Id, "Capture", payment.CaptureId, payment.CapturedAmount ?? 0m, payment.Currency);
            }

            foreach (var refund in payment.Refunds)
            {
                if (WithinRange(refund.CreatedAt, from, to))
                {
                    eShopByTransactionId[refund.PayPalRefundId] = new ReconciliationEShopEntry(
                        order.Id, "Refund", refund.PayPalRefundId, refund.Amount, payment.Currency);
                }
            }
        }

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = payPalTransactions.Count
        };

        var matchedEShopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in payPalTransactions)
        {
            if (eShopByTransactionId.TryGetValue(txn.TransactionId, out var eShopEntry))
            {
                matchedEShopIds.Add(txn.TransactionId);
                report.Matched.Add(new ReconciliationMatch(
                    txn.TransactionId, txn.Status, txn.Amount, txn.Currency, eShopEntry.OrderId, eShopEntry.RecordType));
            }
            else
            {
                report.InPayPalNotInEShop.Add(new ReconciliationPayPalEntry(
                    txn.TransactionId, txn.Status, txn.Amount, txn.Currency, txn.InitiatedAt, txn.EventCode));
            }
        }

        foreach (var kvp in eShopByTransactionId)
        {
            if (!matchedEShopIds.Contains(kvp.Key))
            {
                report.InEShopNotInPayPal.Add(kvp.Value);
            }
        }

        return report;
    }

    private static bool WithinRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}
