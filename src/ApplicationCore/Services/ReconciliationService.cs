using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const decimal AmountTolerance = 0.005m;

    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPal;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalClient payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // PayPal's own record for the range — all pages, not just the first.
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // eShop's own record: every order that has a payment.
        var orders = (await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct)).ToList();
        // Match on the globally-unique correlation token we stamped as custom_id (exact string),
        // so historical transactions from other runs that reused a small order id don't false-match.
        var ordersByCustomId = orders
            .Where(o => o.Payment?.PayPalCustomId is not null)
            .GroupBy(o => o.Payment!.PayPalCustomId)
            .ToDictionary(g => g.Key, g => g.First());
        var ordersByCaptureId = orders
            .Where(o => o.Payment?.CaptureId is not null)
            .GroupBy(o => o.Payment!.CaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<PayPalOnlyTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = MatchOrder(txn, ordersByCustomId, ordersByCaptureId);
            if (order is not null)
            {
                var eShopAmount = order.Payment!.CapturedAmount ?? order.Payment.Amount;
                matched.Add(new ReconciliationMatch(
                    txn.TransactionId, order.Id, txn.EventCode, txn.Status, txn.Amount, eShopAmount,
                    AmountsAgree(txn, order.Payment)));
                matchedOrderIds.Add(order.Id);
            }
            else
            {
                payPalOnly.Add(new PayPalOnlyTransaction(
                    txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency,
                    txn.InitiatedAt, txn.CustomField, txn.InvoiceId));
            }
        }

        // eShop payments captured within the range that PayPal's report does not show (yet).
        var eShopInRange = orders
            .Where(o => o.Payment?.CapturedAt is DateTimeOffset capturedAt && capturedAt >= from && capturedAt <= to)
            .ToList();
        var eShopOnly = eShopInRange
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new EShopOnlyPayment(
                o.Id, o.Payment!.CaptureId, o.Payment.Status.ToString(), o.Payment.CapturedAmount, o.Payment.CapturedAt))
            .ToList();

        return new ReconciliationReport(
            from, to,
            transactions.Count,
            eShopInRange.Count,
            matched,
            payPalOnly,
            eShopOnly);
    }

    private static Order? MatchOrder(
        PayPalTransaction txn,
        IReadOnlyDictionary<string, Order> ordersByCustomId,
        IReadOnlyDictionary<string, Order> ordersByCaptureId)
    {
        // Primary key: the exact correlation token we stamped onto the PayPal order as custom_id.
        if (!string.IsNullOrEmpty(txn.CustomField) && ordersByCustomId.TryGetValue(txn.CustomField!, out var byCustom))
        {
            return byCustom;
        }
        // Fallback: the capture id PayPal reports as the transaction id.
        if (txn.TransactionId is not null && ordersByCaptureId.TryGetValue(txn.TransactionId, out var byCapture))
        {
            return byCapture;
        }
        return null;
    }

    private static bool AmountsAgree(PayPalTransaction txn, Payment payment)
    {
        var magnitude = Math.Abs(txn.Amount);
        var captured = payment.CapturedAmount ?? payment.Amount;
        if (Math.Abs(magnitude - captured) < AmountTolerance)
        {
            return true;
        }
        // A refund transaction lines up with one of the recorded refund amounts.
        return payment.Refunds.Any(r => Math.Abs(magnitude - r.Amount) < AmountTolerance);
    }
}
