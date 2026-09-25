using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentSettings _settings;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPaymentGateway gateway,
        PaymentSettings settings,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var search = await _gateway.SearchTransactionsAsync(from, to, _settings.CurrencyCode, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), ct);

        // Match on our reference echoed into PayPal's invoice_id / custom_field.
        static string? RefOf(GatewayTransaction t) =>
            !string.IsNullOrWhiteSpace(t.InvoiceId) ? t.InvoiceId :
            !string.IsNullOrWhiteSpace(t.CustomField) ? t.CustomField : null;

        var txByRef = new Dictionary<string, GatewayTransaction>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in search.Transactions)
        {
            var r = RefOf(t);
            if (r is not null && !txByRef.ContainsKey(r))
            {
                txByRef[r] = t;
            }
        }

        var matched = new List<ReconciliationMatch>();
        var eshopOnly = new List<ReconciliationEShopOnly>();
        var matchedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var reference = order.Payment!.ReferenceId;
            if (txByRef.TryGetValue(reference, out var tx))
            {
                matchedRefs.Add(reference);
                matched.Add(new ReconciliationMatch(
                    order.Id, reference, order.PaymentStatus.ToString(),
                    order.Payment.CapturedAmount ?? 0m,
                    tx.TransactionId, tx.Status, tx.Amount));
            }
            else
            {
                eshopOnly.Add(new ReconciliationEShopOnly(
                    order.Id, reference, order.PaymentStatus.ToString(), order.Payment.CapturedAmount));
            }
        }

        var payPalOnly = search.Transactions
            .Where(t =>
            {
                var r = RefOf(t);
                return r is null || !matchedRefs.Contains(r);
            })
            .Select(t => new ReconciliationPayPalOnly(t.TransactionId, t.Status, t.Amount, t.CurrencyCode, t.InvoiceId, t.InitiatedAt))
            .ToList();

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {search.Transactions.Count} PayPal txns over {search.PagesScanned}/{search.TotalPages} pages, {orders.Count} eShop orders; matched {matched.Count}, eShop-only {eshopOnly.Count}, PayPal-only {payPalOnly.Count}. complete={search.Complete}");

        return new ReconciliationReport(
            from, to, _settings.CurrencyCode,
            search.Transactions.Count, orders.Count,
            search.Complete, search.PagesScanned, search.TotalPages,
            matched, eshopOnly, payPalOnly);
    }
}
