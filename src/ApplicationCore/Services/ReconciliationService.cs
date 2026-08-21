using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds a reconciliation report by pulling PayPal's own transaction record for a range (paged over the
/// whole range by the PayPal boundary) and lining it up against eShop's captured payments and refunds.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPayPalPaymentService _payPal;

    public ReconciliationService(IReadRepository<Payment> paymentRepository, IPayPalPaymentService payPal)
    {
        _paymentRepository = paymentRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var transactions = await _payPal.SearchTransactionsAsync(startDate, endDate, ct);
        var payPalIds = transactions.Select(t => t.TransactionId).ToHashSet(StringComparer.Ordinal);

        var payments = await _paymentRepository.ListAsync(ct);

        // Map every transaction id eShop knows (captures and refunds) back to its order.
        var eShopTxnToOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                eShopTxnToOrder[payment.CaptureId] = payment.OrderId;
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.PayPalRefundId))
                {
                    eShopTxnToOrder[refund.PayPalRefundId!] = payment.OrderId;
                }
            }
        }

        var lines = transactions
            .Select(t => new ReconciliationLine(
                t.TransactionId, t.Amount, t.CurrencyCode, t.Status, t.InitiationDate,
                eShopTxnToOrder.TryGetValue(t.TransactionId, out var orderId) ? orderId : null))
            .ToList();

        var payPalWithoutOrder = lines.Where(l => l.MatchedOrderId is null).ToList();

        // eShop captures made within the range that PayPal's report did not return.
        var eShopWithoutPayPal = payments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId)
                        && p.CreatedDate >= from && p.CreatedDate <= to
                        && !payPalIds.Contains(p.CaptureId!))
            .Select(p => new UnmatchedEShopPayment(
                p.OrderId, p.CaptureId, p.CapturedGrossAmount, p.CurrencyCode, p.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, lines, payPalWithoutOrder, eShopWithoutPayPal);
    }
}
