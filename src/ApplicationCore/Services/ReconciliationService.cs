using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _gateway;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPaymentGateway gateway,
        IRepository<OrderPayment> paymentRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.", nameof(to));

        // PayPal's own record for the range (covers the whole range across pages).
        var search = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop's side, filtered on the same money-movement clock (capture time).
        var localPayments = await _paymentRepository.ListAsync(new CapturedPaymentsInRangeSpecification(from, to), ct);
        var localByInvoice = localPayments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in search.Transactions)
        {
            // PayPal echoes our invoice id in either invoice_id or custom_field.
            var invoiceId = FirstNonEmpty(tx.InvoiceId, tx.CustomField);
            OrderPayment? local = null;
            if (invoiceId is not null)
                localByInvoice.TryGetValue(invoiceId, out local);

            if (local is not null)
            {
                matchedInvoices.Add(local.InvoiceId);
                lines.Add(new ReconciliationLine
                {
                    Status = ReconciliationStatus.Matched,
                    InvoiceId = local.InvoiceId,
                    OrderId = local.OrderId,
                    PayPalTransactionId = tx.TransactionId,
                    PayPalAmount = tx.Amount,
                    EShopCapturedAmount = local.CapturedAmount,
                    Currency = tx.Currency ?? local.Currency,
                    PayPalStatus = tx.Status,
                    PayPalInitiatedAt = tx.InitiatedAt
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Status = ReconciliationStatus.InPayPalOnly,
                    InvoiceId = invoiceId,
                    OrderId = TryParseOrderId(invoiceId),
                    PayPalTransactionId = tx.TransactionId,
                    PayPalAmount = tx.Amount,
                    Currency = tx.Currency,
                    PayPalStatus = tx.Status,
                    PayPalInitiatedAt = tx.InitiatedAt
                });
            }
        }

        // eShop payments captured in range that PayPal did not report.
        foreach (var local in localPayments.Where(p => !matchedInvoices.Contains(p.InvoiceId)))
        {
            lines.Add(new ReconciliationLine
            {
                Status = ReconciliationStatus.InEShopOnly,
                InvoiceId = local.InvoiceId,
                OrderId = local.OrderId,
                EShopCapturedAmount = local.CapturedAmount,
                Currency = local.Currency
            });
        }

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            Lines = lines,
            PayPalTransactionCount = search.Transactions.Count,
            MatchedCount = lines.Count(l => l.Status == ReconciliationStatus.Matched),
            InPayPalOnlyCount = lines.Count(l => l.Status == ReconciliationStatus.InPayPalOnly),
            InEShopOnlyCount = lines.Count(l => l.Status == ReconciliationStatus.InEShopOnly),
            PagesRead = search.PagesRead,
            TotalPages = search.TotalPages,
            Truncated = search.Truncated
        };

        _logger.LogInformation($"Reconciliation {from:o}..{to:o}: paypal={report.PayPalTransactionCount} matched={report.MatchedCount} paypalOnly={report.InPayPalOnlyCount} eshopOnly={report.InEShopOnlyCount} pages={report.PagesRead}/{report.TotalPages} truncated={report.Truncated}.");
        return report;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static int? TryParseOrderId(string? invoiceId)
    {
        // Invoice ids look like "eshop-{orderId}-{token}". Parse the order-id segment for display.
        if (invoiceId is null) return null;
        const string prefix = "eshop-";
        if (!invoiceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = invoiceId.Substring(prefix.Length);
        var dash = rest.IndexOf('-');
        var orderPart = dash >= 0 ? rest.Substring(0, dash) : rest;
        return int.TryParse(orderPart, out var id) ? id : null;
    }
}
