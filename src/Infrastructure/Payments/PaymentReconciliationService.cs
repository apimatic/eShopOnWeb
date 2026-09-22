using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Lines PayPal's own transaction records for a date range up against eShop payments, matching on the
/// invoice id, so a payment PayPal knows about and eShop doesn't — or the reverse — is visible. Covers
/// the whole range (the gateway walks every page/window).
/// </summary>
public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<OrderPayment> _payments;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(IPayPalPaymentGateway gateway,
        IReadRepository<OrderPayment> payments, ILogger<PaymentReconciliationService> logger)
    {
        _gateway = gateway;
        _payments = payments;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from)
            throw new ArgumentException("'to' must be after 'from'.", nameof(to));

        var search = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop payments that reached PayPal (authorized or beyond), keyed by the invoice id we sent.
        var eshopPayments = (await _payments.ListAsync(ct))
            .Where(p => !string.IsNullOrEmpty(p.PayPalOrderId))
            .ToList();
        var eshopByInvoice = eshopPayments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var seenInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PayPal side: matched vs only-in-paypal.
        foreach (var tx in search.Transactions)
        {
            OrderPayment? match = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId) && eshopByInvoice.TryGetValue(tx.InvoiceId!, out var found))
            {
                match = found;
                seenInvoices.Add(tx.InvoiceId!);
            }

            lines.Add(new ReconciliationLine(
                PayPalTransactionId: tx.TransactionId,
                InvoiceId: tx.InvoiceId,
                OrderId: match?.OrderId,
                PayPalAmount: tx.Amount,
                EShopAmount: match?.CapturedAmount ?? match?.AuthorizedAmount,
                PayPalStatus: tx.Status,
                EShopStatus: match?.Status.ToString(),
                Classification: match is null ? "only-in-paypal" : "matched"));
        }

        // eShop side within the reconciliation window that PayPal did not report — surfaced so the
        // operator can see it. (Sandbox reporting lags, so a just-created payment appearing here is expected.)
        foreach (var p in eshopPayments)
        {
            if (!string.IsNullOrEmpty(p.InvoiceId) && seenInvoices.Contains(p.InvoiceId!)) continue;
            if (p.UpdatedAt < from || p.UpdatedAt > to) continue;

            lines.Add(new ReconciliationLine(
                PayPalTransactionId: null,
                InvoiceId: p.InvoiceId,
                OrderId: p.OrderId,
                PayPalAmount: null,
                EShopAmount: p.CapturedAmount ?? p.AuthorizedAmount,
                PayPalStatus: null,
                EShopStatus: p.Status.ToString(),
                Classification: "only-in-eshop"));
        }

        _logger.LogInformation(
            "Reconciliation {From}..{To}: {PayPal} PayPal txns, {EShop} eShop payments, pages={Pages}, truncated={Truncated}.",
            from, to, search.Transactions.Count, eshopPayments.Count, search.PagesScanned, search.Truncated);

        return new ReconciliationReport(from, to, search.Transactions.Count, eshopPayments.Count,
            search.PagesScanned, search.Truncated, lines);
    }
}
