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

/// <summary>
/// Builds the reconciliation report: it pulls PayPal's own transaction record for the range and lines
/// it up against eShop orders that captured a payment, so a payment PayPal knows about and eShop
/// doesn't — or the reverse — is visible. Orders are matched to PayPal transactions by the reference
/// we send as the invoice_id (falling back to custom_field, which carries the order id).
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalReportingGateway _reportingGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalReportingGateway reportingGateway, IReadRepository<Order> orderRepository)
    {
        _reportingGateway = reportingGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _reportingGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new CapturedOrdersByDateRangeSpecification(from, to), cancellationToken);

        // eShop orders indexed by the unique reference we sent to PayPal as the invoice_id. We match on
        // that reference only: the custom_id (a bare order id) is not unique across runs/accounts and
        // would produce false matches, so it is deliberately not used as a matching key.
        var ordersByReference = new Dictionary<string, Order>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (order.Payment?.Reference is { } reference)
            {
                ordersByReference[reference] = order;
            }
        }

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.Ordinal);

        // Every PayPal transaction: matched to an eShop order, or flagged as unknown to eShop.
        foreach (var txn in transactions)
        {
            var reference = txn.InvoiceId;
            Order? order = null;
            if (!string.IsNullOrEmpty(reference))
            {
                ordersByReference.TryGetValue(reference, out order);
            }

            if (order is not null)
            {
                matchedReferences.Add(order.Payment!.Reference);
                lines.Add(new ReconciliationLine(
                    ReconciliationStatus.Matched,
                    order.Payment!.Reference,
                    order.Id,
                    order.Payment!.CapturedGross ?? order.Payment!.Amount,
                    txn.TransactionId,
                    txn.Amount,
                    txn.Status,
                    string.IsNullOrEmpty(txn.Currency) ? order.Payment!.Currency : txn.Currency));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationStatus.MissingInEShop,
                    reference ?? txn.TransactionId,
                    null,
                    null,
                    txn.TransactionId,
                    txn.Amount,
                    txn.Status,
                    txn.Currency));
            }
        }

        // eShop orders that captured a payment PayPal's report does not (yet) show.
        foreach (var order in orders)
        {
            var reference = order.Payment?.Reference;
            if (reference is null || matchedReferences.Contains(reference))
            {
                continue;
            }

            lines.Add(new ReconciliationLine(
                ReconciliationStatus.MissingInPayPal,
                reference,
                order.Id,
                order.Payment!.CapturedGross ?? order.Payment!.Amount,
                null,
                null,
                null,
                order.Payment!.Currency));
        }

        return new ReconciliationReport(from, to, lines)
        {
            MatchedCount = lines.Count(l => l.Status == ReconciliationStatus.Matched),
            MissingInEShopCount = lines.Count(l => l.Status == ReconciliationStatus.MissingInEShop),
            MissingInPayPalCount = lines.Count(l => l.Status == ReconciliationStatus.MissingInPayPal),
        };
    }
}
