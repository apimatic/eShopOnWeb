using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Lines PayPal's own transaction records up against eShop payments over a date range so a payment
/// PayPal knows about and eShop doesn't — or the reverse — becomes visible. Both sides are filtered
/// on the PayPal transaction time; the PayPal side covers every page of the range.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(IReadRepository<OrderPayment> paymentRepository, IPayPalGateway gateway,
        ILogger<ReconciliationService> logger)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.");

        var search = await _gateway.SearchTransactionsAsync(from, to, ct);
        var localPayments = await _paymentRepository.ListAsync(new ReconcilablePaymentsSpec(from, to), ct);

        // Local payments keyed by the invoice id we handed PayPal.
        var localByInvoice = localPayments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotEshop = new List<ReconciliationMatch>();
        var seenInvoices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var txn in search.Transactions)
        {
            var invoiceKey = txn.InvoiceId;
            OrderPayment? local = null;
            if (!string.IsNullOrEmpty(invoiceKey) && localByInvoice.TryGetValue(invoiceKey, out var byInvoice))
            {
                local = byInvoice;
            }
            else if (int.TryParse(txn.CustomField, out var orderIdFromCustom))
            {
                // Fall back to the custom_id (eShop order id) we also stamped on the PayPal order.
                local = localPayments.FirstOrDefault(p => p.OrderId == orderIdFromCustom);
                invoiceKey = local?.InvoiceId ?? invoiceKey;
            }

            if (local is not null)
            {
                if (invoiceKey is not null) seenInvoices.Add(invoiceKey);
                matched.Add(new ReconciliationMatch(local.InvoiceId, local.OrderId, local.Status.ToString(),
                    local.CapturedAmount ?? local.Amount, txn.TransactionId, txn.Amount, txn.Status));
            }
            else
            {
                inPayPalNotEshop.Add(new ReconciliationMatch(txn.InvoiceId, null, null, null,
                    txn.TransactionId, txn.Amount, txn.Status));
            }
        }

        var inEshopNotPayPal = localPayments
            .Where(p => string.IsNullOrEmpty(p.InvoiceId) || !seenInvoices.Contains(p.InvoiceId))
            .Select(p => new ReconciliationMatch(p.InvoiceId, p.OrderId, p.Status.ToString(),
                p.CapturedAmount ?? p.Amount, null, null, null))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From:o}..{To:o}: {PayPal} PayPal txns ({Pages} pages), {Matched} matched, {PpOnly} PayPal-only, {EshopOnly} eShop-only.",
            from, to, search.Transactions.Count, search.PagesFetched, matched.Count, inPayPalNotEshop.Count, inEshopNotPayPal.Count);

        return new ReconciliationReport(from, to, search.Transactions.Count, search.PagesFetched, search.Truncated,
            matched, inPayPalNotEshop, inEshopNotPayPal);
    }
}
