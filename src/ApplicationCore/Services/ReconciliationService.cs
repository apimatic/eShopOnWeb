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
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ValidationException("Reconciliation 'to' must not be earlier than 'from'.");
        }

        // PayPal side: every transaction PayPal recorded across the whole range (all pages).
        var payPalResult = await _payPal.ListTransactionsAsync(from, to, cancellationToken);

        // eShop side: captures and refunds recorded against orders placed in the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpecification(from, to), cancellationToken);

        // Index the eShop money movements by their PayPal reference (capture id / refund id).
        var eShopByReference = new Dictionary<string, EShopOnlyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                eShopByReference[payment.CaptureId!] = new EShopOnlyEntry(
                    order.Id, "Capture", payment.CaptureId!, payment.CapturedAmount ?? payment.Amount, payment.CaptureStatus ?? "");
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.PayPalRefundId))
                {
                    eShopByReference[refund.PayPalRefundId] = new EShopOnlyEntry(
                        order.Id, "Refund", refund.PayPalRefundId, refund.Amount, refund.Status);
                }
            }
        }

        var matched = new List<MatchedTransaction>();
        var payPalOnly = new List<PayPalOnlyTransaction>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in payPalResult.Transactions)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId) && eShopByReference.TryGetValue(txn.TransactionId, out var entry))
            {
                seenReferences.Add(txn.TransactionId);
                matched.Add(new MatchedTransaction(
                    txn.TransactionId,
                    entry.OrderId,
                    entry.Kind,
                    txn.Status,
                    txn.Amount,
                    entry.Amount,
                    AmountsAgree(txn.Amount, entry.Amount)));
            }
            else
            {
                payPalOnly.Add(new PayPalOnlyTransaction(txn.TransactionId, txn.Status, txn.Amount, txn.Currency, txn.Date));
            }
        }

        // eShop entries PayPal's report did not show (e.g. reporting lag on very recent activity).
        var eShopOnly = eShopByReference
            .Where(kvp => !seenReferences.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .OrderBy(e => e.OrderId)
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            payPalResult.PagesRead,
            payPalResult.Transactions.Count,
            matched,
            payPalOnly,
            eShopOnly);
    }

    // PayPal reports capture amounts as positive and refunds as negative; compare on magnitude.
    private static bool AmountsAgree(decimal payPalAmount, decimal eShopAmount) =>
        Math.Abs(Math.Abs(payPalAmount) - Math.Abs(eShopAmount)) < 0.005m;
}
