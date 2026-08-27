using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPalClient, IRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentConflictException("The 'to' timestamp must be after the 'from' timestamp.");
        }

        var transactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        // Index eShop payment state by every PayPal-owned identifier we hold.
        var orderByAuthorization = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var orderByCapture = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var orderByRefund = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var orderByInvoice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var payment = order.Payment!;
            orderByAuthorization[payment.AuthorizationId] = order.Id;
            if (payment.CaptureId is not null)
            {
                orderByCapture[payment.CaptureId] = order.Id;
            }
            foreach (var refund in payment.Refunds)
            {
                orderByRefund[refund.PayPalRefundId] = order.Id;
            }
            orderByInvoice[payment.InvoiceId] = order.Id;
        }

        var entries = new List<ReconciliationEntry>();
        var seenTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            seenTransactionIds.Add(txn.TransactionId);

            int? matchedOrderId = null;
            var matchType = "none";
            if (txn.TransactionId is not null && orderByCapture.TryGetValue(txn.TransactionId, out var byCapture))
            {
                matchedOrderId = byCapture;
                matchType = "capture";
            }
            else if (txn.TransactionId is not null && orderByRefund.TryGetValue(txn.TransactionId, out var byRefund))
            {
                matchedOrderId = byRefund;
                matchType = "refund";
            }
            else if (txn.TransactionId is not null && orderByAuthorization.TryGetValue(txn.TransactionId, out var byAuth))
            {
                matchedOrderId = byAuth;
                matchType = "authorization";
            }
            else if (txn.InvoiceId is not null && orderByInvoice.TryGetValue(txn.InvoiceId, out var byInvoice))
            {
                matchedOrderId = byInvoice;
                matchType = "invoice";
            }

            entries.Add(new ReconciliationEntry(
                txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency, txn.Fee,
                txn.InvoiceId, txn.InitiationTime, matchedOrderId, matchType));
        }

        // eShop payments in range that PayPal's report does not know about.
        var unmatched = new List<UnmatchedEShopOrder>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            var inRange = (payment.CapturedAt ?? payment.AuthorizedAt) >= from && (payment.CapturedAt ?? payment.AuthorizedAt) <= to;
            if (!inRange)
            {
                continue;
            }

            var knownIds = new List<string> { payment.AuthorizationId };
            if (payment.CaptureId is not null)
            {
                knownIds.Add(payment.CaptureId);
            }
            knownIds.AddRange(payment.Refunds.Select(r => r.PayPalRefundId));

            if (!knownIds.Any(seenTransactionIds.Contains))
            {
                unmatched.Add(new UnmatchedEShopOrder(
                    order.Id,
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    payment.Refunds.Select(r => r.PayPalRefundId).ToList()));
            }
        }

        return new ReconciliationReport(from, to, transactions.Count, entries, unmatched);
    }
}
