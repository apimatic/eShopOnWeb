using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Lines PayPal's transaction report up against eShop's own record of captured payments for a date
/// range, so a payment one side knows about and the other does not is visible. The matching key is
/// the merchant invoice id eShop sends to PayPal on every order.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPayPalClient payPal,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _payPal.ListTransactionsAsync(from, to, null, cancellationToken);

        // eShop side: orders captured within the range, keyed by the invoice id we sent to PayPal.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        var eshopByInvoice = orders
            .Where(o => o.Payment is { CaptureId: not null } p && p.CapturedAt is { } at && at >= from && at <= to)
            .ToDictionary(o => o.Payment!.InvoiceId, o => o, StringComparer.Ordinal);

        // PayPal side: transactions grouped by invoice id (some may carry none).
        var payPalWithInvoice = transactions.Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .GroupBy(t => t.InvoiceId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var payPalWithoutInvoice = transactions.Where(t => string.IsNullOrEmpty(t.InvoiceId)).ToList();

        var matched = new List<ReconciliationEntry>();
        var inPayPalNotInEShop = new List<ReconciliationEntry>();
        var inEShopNotInPayPal = new List<ReconciliationEntry>();

        var allInvoices = new HashSet<string>(eshopByInvoice.Keys, StringComparer.Ordinal);
        allInvoices.UnionWith(payPalWithInvoice.Keys);

        foreach (var invoice in allInvoices)
        {
            var hasEShop = eshopByInvoice.TryGetValue(invoice, out var order);
            var hasPayPal = payPalWithInvoice.TryGetValue(invoice, out var txns);

            var entry = BuildEntry(invoice, hasEShop ? order : null, hasPayPal ? txns : null);

            if (hasEShop && hasPayPal)
            {
                matched.Add(entry);
            }
            else if (hasPayPal)
            {
                inPayPalNotInEShop.Add(entry);
            }
            else
            {
                inEShopNotInPayPal.Add(entry);
            }
        }

        // Transactions PayPal reports with no invoice id cannot be tied to an eShop order.
        foreach (var t in payPalWithoutInvoice)
        {
            inPayPalNotInEShop.Add(new ReconciliationEntry(
                null, null, t.TransactionId, t.EventCode, t.Status, t.Amount, null, null, t.CurrencyCode));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {matched.Count} matched, {inPayPalNotInEShop.Count} PayPal-only, {inEShopNotInPayPal.Count} eShop-only.");

        return new ReconciliationReport(from, to, matched, inPayPalNotInEShop, inEShopNotInPayPal);
    }

    private static ReconciliationEntry BuildEntry(string invoice, Order? order, List<PayPalTransaction>? txns)
    {
        var payment = order?.Payment;
        string? currency = payment?.CurrencyCode ?? txns?.FirstOrDefault()?.CurrencyCode;

        return new ReconciliationEntry(
            InvoiceId: invoice,
            OrderId: order?.Id,
            PayPalTransactionId: txns is null ? null : string.Join(",", txns.Select(t => t.TransactionId)),
            PayPalEventCode: txns?.FirstOrDefault()?.EventCode,
            PayPalStatus: txns?.FirstOrDefault()?.Status,
            PayPalAmount: txns?.Sum(t => t.Amount),
            EShopAmount: payment?.CapturedAmount,
            EShopPaymentStatus: payment?.Status.ToString(),
            CurrencyCode: currency);
    }
}
