using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction records up against eShop's captured/refunded payments for a date
/// range, so a transaction one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _gateway;
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPayPalGateway gateway, IReadRepository<OrderPayment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    private sealed record EShopTransaction(string Id, int OrderId, decimal Amount, string Currency, DateTimeOffset Date, string Kind);

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
            throw new Exceptions.PaymentException("'to' must not be earlier than 'from'.", Exceptions.PaymentErrorKind.Validation);

        // PayPal's own record for the range (covers the whole range via pagination in the gateway).
        var payPalTransactions = await _gateway.ListTransactionsAsync(from, to, ct);
        var payPalIds = payPalTransactions
            .Where(t => !string.IsNullOrEmpty(t.TransactionId))
            .Select(t => t.TransactionId!)
            .ToHashSet(StringComparer.Ordinal);

        // eShop's own money movements (captures and refunds) that fall in the range.
        var allPayments = await _paymentRepository.ListAsync(ct);
        var eShopTransactions = new List<EShopTransaction>();
        foreach (var payment in allPayments)
        {
            if (payment.CaptureId is not null && payment.CapturedAmount is not null &&
                payment.CreatedDate >= from && payment.CreatedDate <= to)
            {
                eShopTransactions.Add(new EShopTransaction(
                    payment.CaptureId, payment.OrderId, payment.CapturedAmount.Value, payment.Currency, payment.CreatedDate, "Capture"));
            }

            foreach (var refund in payment.Refunds.Where(r => r.CreatedDate >= from && r.CreatedDate <= to))
            {
                eShopTransactions.Add(new EShopTransaction(
                    refund.PayPalRefundId, payment.OrderId, refund.Amount, payment.Currency, refund.CreatedDate, "Refund"));
            }
        }

        var eShopById = eShopTransactions
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var lines = new List<ReconciliationLine>();
        int matched = 0, payPalOnly = 0, eShopOnly = 0;

        foreach (var txn in payPalTransactions)
        {
            var id = txn.TransactionId;
            var isMatched = id is not null && eShopById.ContainsKey(id);
            if (isMatched) matched++; else payPalOnly++;

            lines.Add(new ReconciliationLine(
                Source: isMatched ? "Matched" : "PayPalOnly",
                PayPalTransactionId: id,
                Status: txn.Status,
                Amount: txn.Amount,
                CurrencyCode: txn.CurrencyCode,
                Date: txn.Date,
                OrderId: isMatched ? eShopById[id!].OrderId : null));
        }

        foreach (var eShopTxn in eShopTransactions.Where(t => !payPalIds.Contains(t.Id)))
        {
            eShopOnly++;
            lines.Add(new ReconciliationLine(
                Source: "EShopOnly",
                PayPalTransactionId: eShopTxn.Id,
                Status: eShopTxn.Kind,
                Amount: eShopTxn.Amount,
                CurrencyCode: eShopTxn.Currency,
                Date: eShopTxn.Date,
                OrderId: eShopTxn.OrderId));
        }

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly, lines);
    }
}
