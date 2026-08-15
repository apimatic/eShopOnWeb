using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's transaction report for a date range up against eShop's own payment records,
/// matching on the eShop-owned invoice id, and surfaces the three interesting buckets: matched,
/// present in PayPal only, and present in eShop only.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Order> orderRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's own view — paged across the whole range by the client.
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // eShop's own view — orders placed in the window that carry a PayPal payment.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), ct);

        // Group PayPal transactions by the eShop invoice id they carry (money-movement events only).
        var payPalByInvoice = transactions
            .Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .GroupBy(t => t.InvoiceId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var matched = new List<ReconciliationRow>();
        var inEShopOnly = new List<ReconciliationRow>();
        var matchedInvoiceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in orders)
        {
            var payment = order.Payment!;
            var invoiceId = payment.InvoiceId;

            if (invoiceId is not null && payPalByInvoice.TryGetValue(invoiceId, out var txns))
            {
                matchedInvoiceIds.Add(invoiceId);
                // Prefer the captured/completed transaction if present.
                var txn = txns.FirstOrDefault(t => string.Equals(t.Status, "S", StringComparison.OrdinalIgnoreCase))
                          ?? txns[0];
                var eshopAmount = payment.CapturedAmount ?? payment.AuthorizedAmount;
                var discrepancy = DescribeAmountDiscrepancy(eshopAmount, txn.Amount);
                matched.Add(new ReconciliationRow(
                    invoiceId, order.Id, order.Status.ToString(), payment.CapturedAmount,
                    payment.PayPalOrderId, payment.CaptureId,
                    txn.TransactionId, txn.Status, txn.Amount, txn.Currency, txn.InitiationDate, discrepancy));
            }
            else
            {
                // eShop recorded a payment PayPal's report does not show in this range. During sandbox
                // reporting lag this is expected for very recent payments; over a settled range it is a gap.
                inEShopOnly.Add(new ReconciliationRow(
                    invoiceId, order.Id, order.Status.ToString(), payment.CapturedAmount,
                    payment.PayPalOrderId, payment.CaptureId,
                    null, null, null, payment.Currency, null,
                    "Present in eShop but not found in PayPal's report for this range."));
            }
        }

        // PayPal transactions whose eShop invoice id we do not recognise (or that carry none).
        var inPayPalOnly = new List<ReconciliationRow>();
        foreach (var txn in transactions)
        {
            var invoiceId = txn.InvoiceId;
            if (invoiceId is not null && matchedInvoiceIds.Contains(invoiceId))
            {
                continue;
            }
            inPayPalOnly.Add(new ReconciliationRow(
                invoiceId, null, null, null, null, null,
                txn.TransactionId, txn.Status, txn.Amount, txn.Currency, txn.InitiationDate,
                invoiceId is null
                    ? "PayPal transaction with no eShop invoice reference."
                    : "Present in PayPal but no matching eShop order."));
        }

        _logger.LogInformation(
            "Reconciliation {0:o}..{1:o}: {2} PayPal txns, {3} matched, {4} PayPal-only, {5} eShop-only.",
            from, to, transactions.Count, matched.Count, inPayPalOnly.Count, inEShopOnly.Count);

        return new ReconciliationReport(from, to, transactions.Count, matched.Count,
            matched, inPayPalOnly, inEShopOnly);
    }

    private static string DescribeAmountDiscrepancy(decimal eshopAmount, decimal? payPalAmount)
    {
        if (payPalAmount is null)
        {
            return "PayPal transaction has no amount.";
        }
        return Math.Abs(eshopAmount - payPalAmount.Value) <= 0.001m
            ? "Matched."
            : $"Amount mismatch: eShop {eshopAmount:0.00} vs PayPal {payPalAmount.Value:0.00}.";
    }
}
