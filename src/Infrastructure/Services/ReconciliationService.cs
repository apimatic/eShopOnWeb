using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Lines PayPal's own transactions for a date range up against eShop order payments, matching on the eShop
/// invoice reference stamped on each order. Covers the whole range (all pages), and surfaces both directions
/// of drift: a PayPal transaction with no eShop order, and an eShop payment PayPal does not report.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private const int MaxPages = 1000;

    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<ReconciliationService> _logger;
    private readonly string _currency;

    public ReconciliationService(
        IReadRepository<OrderPayment> paymentRepository,
        IPayPalGateway gateway,
        IOptions<PayPalOptions> options,
        ILogger<ReconciliationService> logger)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(options.Value.Currency) ? "USD" : options.Value.Currency!.Trim().ToUpperInvariant();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var search = await _gateway.SearchTransactionsAsync(from, to, _currency, MaxPages, ct);
        var payments = await _paymentRepository.ListAsync(new PaidOrderPaymentsSpec(), ct);

        var byInvoice = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        foreach (var p in payments)
            byInvoice[p.InvoiceId] = p;

        var matched = new List<ReconciliationMatch>();
        var onlyInPayPal = new List<PayPalOnlyTransaction>();
        var matchedInvoices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tx in search.Transactions)
        {
            if (tx.InvoiceId is not null && byInvoice.TryGetValue(tx.InvoiceId, out var payment))
            {
                matchedInvoices.Add(tx.InvoiceId);
                var eshopAmount = payment.CapturedAmount ?? payment.Amount;
                var agree = tx.Amount.HasValue && Math.Abs(Math.Abs(tx.Amount.Value) - eshopAmount) < 0.01m;
                matched.Add(new ReconciliationMatch(
                    tx.InvoiceId, tx.TransactionId, tx.Amount, tx.Status,
                    payment.OrderId, eshopAmount, payment.State.ToString(), agree));
            }
            else
            {
                onlyInPayPal.Add(new PayPalOnlyTransaction(
                    tx.TransactionId, tx.InvoiceId, tx.Amount, tx.CurrencyCode, tx.Status, tx.InitiationDate));
            }
        }

        var onlyInEshop = payments
            .Where(p => !matchedInvoices.Contains(p.InvoiceId))
            .Select(p => new EshopOnlyPayment(p.OrderId, p.InvoiceId, p.CapturedAmount ?? p.Amount, p.State.ToString(), p.PayPalOrderId))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {From}..{To}: {PayPalCount} PayPal txns ({Pages}/{TotalPages} pages, complete={Complete}); matched={Matched}, paypalOnly={PayPalOnly}, eshopOnly={EshopOnly}",
            from, to, search.Transactions.Count, search.PagesFetched, search.TotalPages, search.Complete,
            matched.Count, onlyInPayPal.Count, onlyInEshop.Count);

        return new ReconciliationReport(
            from, to, search.Complete, search.PagesFetched, search.TotalPages,
            search.Transactions.Count, matched, onlyInPayPal, onlyInEshop);
    }
}
