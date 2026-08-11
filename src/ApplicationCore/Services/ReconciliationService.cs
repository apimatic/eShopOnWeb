using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own transaction records against eShop's captured orders over a date range.
/// The gateway pages the whole range; here the two sides are joined on the eShop order id, which is
/// stamped onto every PayPal order as both invoice_id and custom_id at authorize time.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public ReconciliationService(IRepository<Payment> paymentRepository, IPayPalPaymentGateway gateway)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var captured = await _paymentRepository.ListAsync(new CapturedPaymentsBetweenSpec(from, to), ct);

        var transactionsByOrder = new Dictionary<int, List<GatewayTransaction>>();
        var unlinkedTransactions = new List<GatewayTransaction>();
        foreach (var t in transactions)
        {
            if (TryResolveOrderId(t, out var orderId))
            {
                if (!transactionsByOrder.TryGetValue(orderId, out var list))
                {
                    transactionsByOrder[orderId] = list = new List<GatewayTransaction>();
                }
                list.Add(t);
            }
            else
            {
                unlinkedTransactions.Add(t);
            }
        }

        var capturedOrderIds = captured.Select(p => p.OrderId).ToHashSet();
        var lines = new List<ReconciliationLine>();

        // eShop side: matched, or captured here but absent from PayPal's report.
        foreach (var payment in captured)
        {
            if (transactionsByOrder.TryGetValue(payment.OrderId, out var matches))
            {
                var representative = matches.OrderByDescending(m => m.Amount).First();
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.Matched,
                    OrderId = payment.OrderId,
                    PayPalCaptureId = payment.CaptureId,
                    PayPalTransactionId = representative.TransactionId,
                    EShopAmount = payment.CapturedGross,
                    PayPalAmount = representative.Amount,
                    Currency = payment.Currency,
                    PayPalStatus = representative.Status
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.InEShopOnly,
                    OrderId = payment.OrderId,
                    PayPalCaptureId = payment.CaptureId,
                    EShopAmount = payment.CapturedGross,
                    Currency = payment.Currency,
                    Note = "eShop captured this order but PayPal's report for the range does not (yet) include it. PayPal reporting lags live activity."
                });
            }
        }

        // PayPal side: transactions PayPal reports that eShop has no captured order for.
        foreach (var (orderId, orderTransactions) in transactionsByOrder)
        {
            if (capturedOrderIds.Contains(orderId)) continue;
            foreach (var t in orderTransactions)
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.InPayPalOnly,
                    OrderId = orderId,
                    PayPalTransactionId = t.TransactionId,
                    PayPalAmount = t.Amount,
                    Currency = t.Currency,
                    PayPalStatus = t.Status,
                    Note = "PayPal reports a transaction referencing this order id that eShop has no captured payment for."
                });
            }
        }

        // PayPal transactions not linked to any eShop order at all.
        foreach (var t in unlinkedTransactions)
        {
            lines.Add(new ReconciliationLine
            {
                Match = ReconciliationMatch.InPayPalOnly,
                PayPalTransactionId = t.TransactionId,
                PayPalAmount = t.Amount,
                Currency = t.Currency,
                PayPalStatus = t.Status,
                Note = "PayPal transaction not linked to any eShop order (no invoice/custom id)."
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            EShopCapturedCount = captured.Count,
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            InPayPalOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.InPayPalOnly),
            InEShopOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.InEShopOnly),
            Lines = lines
        };
    }

    private static bool TryResolveOrderId(GatewayTransaction transaction, out int orderId)
    {
        return int.TryParse(transaction.InvoiceId, out orderId)
            || int.TryParse(transaction.CustomId, out orderId);
    }
}
