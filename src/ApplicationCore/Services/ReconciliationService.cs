using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own transaction record for a date range against eShop's captured orders,
/// surfacing discrepancies in both directions.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new PaymentException("'to' must be after 'from'.", PaymentErrorReason.Validation);
        }

        // PayPal's record for the whole range (chunked + fully paged in the gateway).
        var transactions = await _payPal.ListTransactionsAsync(from, to);

        // eShop's captured orders that fall within the range.
        var capturedOrders = (await _orderRepository.ListAsync(new CapturedOrdersSpecification()))
            .Where(o => o.Payment?.CapturedAt is { } at && at >= from && at <= to)
            .ToList();

        // Index PayPal transactions for matching (by our invoice id and by capture/transaction id).
        var ppByInvoice = new Dictionary<string, PayPalTransaction>(StringComparer.OrdinalIgnoreCase);
        var ppByTxnId = new Dictionary<string, PayPalTransaction>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transactions)
        {
            if (!string.IsNullOrEmpty(t.InvoiceId))
            {
                ppByInvoice[t.InvoiceId!] = t;
            }
            ppByTxnId[t.TransactionId] = t;
        }

        var matched = new List<ReconciliationLine>();
        var inEShopNotPayPal = new List<UnmatchedEShopOrder>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in capturedOrders)
        {
            var payment = order.Payment!;
            var invoiceId = payment.InvoiceId;

            PayPalTransaction? tx = null;
            if (!ppByInvoice.TryGetValue(invoiceId, out tx) && payment.CaptureId is { } capId)
            {
                ppByTxnId.TryGetValue(capId, out tx);
            }

            if (tx is not null)
            {
                matchedTxnIds.Add(tx.TransactionId);
                matched.Add(new ReconciliationLine(
                    OrderId: order.Id,
                    InvoiceId: invoiceId,
                    CaptureTransactionId: payment.CaptureId,
                    EShopCapturedAmount: payment.CapturedAmount ?? 0m,
                    PayPalAmount: tx.Amount,
                    PayPalTransactionId: tx.TransactionId,
                    PayPalStatus: tx.Status));
            }
            else
            {
                inEShopNotPayPal.Add(new UnmatchedEShopOrder(
                    OrderId: order.Id,
                    InvoiceId: invoiceId,
                    CaptureTransactionId: payment.CaptureId,
                    CapturedAmount: payment.CapturedAmount ?? 0m));
            }
        }

        // Everything PayPal knows in the range that we did not match to an eShop captured order.
        var inPayPalNotEShop = transactions
            .Where(t => !matchedTxnIds.Contains(t.TransactionId))
            .Select(t => new UnmatchedPayPalTransaction(
                t.TransactionId, t.InvoiceId, t.Amount, t.Currency, t.Status, t.EventCode, t.Date))
            .ToList();

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            EShopCapturedOrderCount: capturedOrders.Count,
            Matched: matched,
            InPayPalNotInEShop: inPayPalNotEShop,
            InEShopNotInPayPal: inEShopNotPayPal);
    }
}
