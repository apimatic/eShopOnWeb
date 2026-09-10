using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction record for a date range up against eShop's captured
/// payments so that a payment one side knows about and the other doesn't is visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<OrderPayment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidPaymentRequestException("'to' must be on or after 'from'.");
        }

        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);

        var allPayments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpec(), cancellationToken);
        var capturedPayments = allPayments.Where(p => !string.IsNullOrEmpty(p.CaptureId)).ToList();

        // Index eShop payments by every identifier PayPal's report might carry.
        var byCaptureId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        var byRefundId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        var byInvoiceId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        foreach (var p in capturedPayments)
        {
            if (!string.IsNullOrEmpty(p.CaptureId)) byCaptureId[p.CaptureId!] = p;
            if (!string.IsNullOrEmpty(p.PayPalInvoiceId)) byInvoiceId[p.PayPalInvoiceId!] = p;
            foreach (var r in p.Refunds)
            {
                if (!string.IsNullOrEmpty(r.PayPalRefundId)) byRefundId[r.PayPalRefundId] = p;
            }
        }

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var orderId = OrderInvoiceReference.TryParseOrderId(txn.InvoiceId);
            OrderPayment? match = null;
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                if (byCaptureId.TryGetValue(txn.TransactionId, out var byCap)) match = byCap;
                else if (byRefundId.TryGetValue(txn.TransactionId, out var byRef)) match = byRef;
            }
            if (match is null && !string.IsNullOrEmpty(txn.InvoiceId))
            {
                byInvoiceId.TryGetValue(txn.InvoiceId!, out match);
            }

            if (match is not null)
            {
                matchedOrderIds.Add(match.OrderId);
                lines.Add(new ReconciliationLine(
                    MatchStatus: "Matched",
                    PayPalTransactionId: txn.TransactionId,
                    PayPalStatus: txn.Status,
                    PayPalAmount: txn.Amount,
                    PayPalFee: txn.Fee,
                    InvoiceId: txn.InvoiceId,
                    OrderId: match.OrderId,
                    EShopPaymentStatus: match.Status.ToString(),
                    EShopCapturedGross: match.CapturedGross));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    MatchStatus: "InPayPalOnly",
                    PayPalTransactionId: txn.TransactionId,
                    PayPalStatus: txn.Status,
                    PayPalAmount: txn.Amount,
                    PayPalFee: txn.Fee,
                    InvoiceId: txn.InvoiceId,
                    OrderId: orderId,
                    EShopPaymentStatus: null,
                    EShopCapturedGross: null));
            }
        }

        // eShop captured payments in the range that PayPal's report does not (yet) show.
        foreach (var payment in capturedPayments.Where(p => p.CreatedDate >= from && p.CreatedDate <= to))
        {
            if (matchedOrderIds.Contains(payment.OrderId))
            {
                continue;
            }
            lines.Add(new ReconciliationLine(
                MatchStatus: "InEShopOnly",
                PayPalTransactionId: null,
                PayPalStatus: null,
                PayPalAmount: null,
                PayPalFee: null,
                InvoiceId: payment.PayPalInvoiceId,
                OrderId: payment.OrderId,
                EShopPaymentStatus: payment.Status.ToString(),
                EShopCapturedGross: payment.CapturedGross));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            EShopCapturedCount: capturedPayments.Count,
            MatchedCount: lines.Count(l => l.MatchStatus == "Matched"),
            InPayPalOnlyCount: lines.Count(l => l.MatchStatus == "InPayPalOnly"),
            InEShopOnlyCount: lines.Count(l => l.MatchStatus == "InEShopOnly"),
            Lines: lines);
    }
}
