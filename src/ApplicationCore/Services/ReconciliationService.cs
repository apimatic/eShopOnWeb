using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Reconciles PayPal's own transaction record for a date range against eShop's captured payments,
/// so a payment one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    /// <summary>The invoice-id prefix the integration stamps onto every PayPal order for matching.</summary>
    public const string InvoicePrefix = "ESHOP-ORDER-";

    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<OrderPayment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation 'to' must not be earlier than 'from'.", nameof(to));
        }

        // PayPal's own record for the range (already paged across the whole range by the gateway).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's captured payments that fall in the same range.
        var allPayments = await _paymentRepository.ListAsync(cancellationToken);
        var eShopCaptured = allPayments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId))
            .Where(p => WithinRange(p, from, to))
            .ToDictionary(p => p.OrderId);

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        // Walk PayPal's transactions; match each to an eShop order where possible.
        foreach (var tx in transactions)
        {
            var orderId = ExtractOrderId(tx);
            if (orderId is { } id && eShopCaptured.TryGetValue(id, out var payment))
            {
                matchedOrderIds.Add(id);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.Matched,
                    tx.TransactionId, tx.Amount, tx.Status, tx.CurrencyCode ?? payment.CurrencyCode,
                    payment.OrderId, payment.CapturedAmount, payment.Status.ToString()));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.InPayPalOnly,
                    tx.TransactionId, tx.Amount, tx.Status, tx.CurrencyCode,
                    orderId, null, null));
            }
        }

        // eShop captured payments PayPal's report did not return.
        foreach (var payment in eShopCaptured.Values.Where(p => !matchedOrderIds.Contains(p.OrderId)))
        {
            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.InEShopOnly,
                null, null, null, payment.CurrencyCode,
                payment.OrderId, payment.CapturedAmount, payment.Status.ToString()));
        }

        return new ReconciliationReport(
            from, to,
            transactions.Count,
            entries.Count(e => e.Match == ReconciliationMatch.Matched),
            entries.Count(e => e.Match == ReconciliationMatch.InPayPalOnly),
            entries.Count(e => e.Match == ReconciliationMatch.InEShopOnly),
            entries);
    }

    private static bool WithinRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        var when = payment.UpdatedDate ?? payment.CreatedDate;
        return when >= from && when <= to;
    }

    private static int? ExtractOrderId(PayPalTransaction tx)
    {
        if (int.TryParse(tx.CustomField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var custom))
        {
            return custom;
        }

        if (!string.IsNullOrEmpty(tx.InvoiceId) && tx.InvoiceId.StartsWith(InvoicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Invoice ids look like "ESHOP-ORDER-{orderId}-{uniqueToken}"; read the order id segment.
            var tail = tx.InvoiceId[InvoicePrefix.Length..];
            var idSegment = tail.Split('-', 2)[0];
            if (int.TryParse(idSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromInvoice))
            {
                return fromInvoice;
            }
        }

        return null;
    }
}
