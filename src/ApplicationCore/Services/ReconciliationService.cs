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
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPayPalGateway payPal,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must not be earlier than 'from'.");
        }

        // PayPal's own record over the whole range (chunked + paged by the gateway).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's captured payments over the same range.
        var capturedOrders = await _orderRepository.ListAsync(
            new CapturedOrdersByDateRangeSpecification(from, to), cancellationToken);

        // Group PayPal transactions by the eShop invoice id we stamped on them.
        var payPalByInvoice = transactions
            .Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .GroupBy(t => t.InvoiceId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var matched = new List<ReconciledEntry>();
        var eShopOnly = new List<EShopOnlyEntry>();
        var matchedInvoiceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in capturedOrders)
        {
            var payment = order.Payment!;
            if (payPalByInvoice.TryGetValue(payment.InvoiceId, out var ppTxns))
            {
                matchedInvoiceIds.Add(payment.InvoiceId);
                matched.Add(new ReconciledEntry(
                    order.Id,
                    payment.InvoiceId,
                    payment.CapturedAmount ?? payment.Amount,
                    ppTxns.Sum(t => t.Amount),
                    ppTxns.Select(t => t.TransactionId).ToList()));
            }
            else
            {
                eShopOnly.Add(new EShopOnlyEntry(
                    order.Id, payment.InvoiceId, payment.CapturedAmount ?? payment.Amount, payment.Status.ToString()));
            }
        }

        // PayPal transactions whose invoice id maps to no captured eShop order in range.
        var payPalOnly = transactions
            .Where(t => string.IsNullOrEmpty(t.InvoiceId) || !matchedInvoiceIds.Contains(t.InvoiceId!))
            .Select(t => new PayPalOnlyEntry(
                t.TransactionId, t.InvoiceId, t.Amount, t.Status, t.EventCode, t.InitiationDate))
            .ToList();

        _logger.LogInformation(
            "Reconciliation {0}..{1}: {2} PayPal txns, {3} eShop captures, {4} matched, {5} eShop-only, {6} PayPal-only",
            from, to, transactions.Count, capturedOrders.Count, matched.Count, eShopOnly.Count, payPalOnly.Count);

        return new ReconciliationReport(from, to, matched, eShopOnly, payPalOnly)
        {
            PayPalTransactionCount = transactions.Count,
            EShopPaymentCount = capturedOrders.Count
        };
    }
}
