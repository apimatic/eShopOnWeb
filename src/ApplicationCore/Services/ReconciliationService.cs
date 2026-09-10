using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IPayPalGateway _gateway;
    private readonly string _currency;

    public ReconciliationService(IReadRepository<OrderPayment> paymentRepository, IPayPalGateway gateway,
        IPayPalCurrencyProvider currencyProvider)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _currency = currencyProvider.Currency;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new Exceptions.PaymentValidationException("'to' must be on or after 'from'.");

        // PayPal's full record for the range (every page).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, _currency, ct);

        // eShop's captured orders, keyed by the unique invoice id we sent to PayPal.
        var captured = await _paymentRepository.ListAsync(new CapturedOrderPaymentsSpec(), ct);
        var capturedByInvoice = captured
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<PayPalTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            if (!string.IsNullOrEmpty(txn.InvoiceId) && capturedByInvoice.TryGetValue(txn.InvoiceId!, out var p))
            {
                matched.Add(new ReconciliationMatch(p.OrderId, p.Status, p.Amount, txn));
                matchedOrderIds.Add(p.OrderId);
            }
            else
            {
                inPayPalOnly.Add(txn);
            }
        }

        var inEShopOnly = captured
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new ReconciliationOrder(p.OrderId, p.Status, p.Amount, p.Currency, p.CaptureId))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched.Count, matched, inPayPalOnly, inEShopOnly);
    }
}
