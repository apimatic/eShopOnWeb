using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction records up against eShop orders over a date range, so a payment
/// PayPal knows about and eShop doesn't — or the reverse — is visible. Matching is by the external
/// reference this app stamps on every PayPal payment (invoice_id / custom field).
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPal, IReadRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // PayPal's record for the whole range (the gateway chunks the range and pages every result).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);

        // Every eShop order that carries a PayPal payment, indexed by its external reference.
        var orders = await _orderRepository.ListAsync(ct);
        var ordersByReference = orders
            .Where(o => o.Payment is not null)
            .ToDictionary(o => o.Payment!.ReferenceId, o => o, StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            var reference = !string.IsNullOrEmpty(tx.InvoiceId) ? tx.InvoiceId : tx.CustomField;

            if (reference is not null && ordersByReference.TryGetValue(reference, out var order))
            {
                matchedReferences.Add(reference);
                var eshopAmount = order.Payment!.CapturedAmount ?? order.Payment.AuthorizedAmount;
                var amountMismatch = tx.Amount > 0m && tx.Amount != eshopAmount;

                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.Matched,
                    tx.TransactionId, tx.Status, tx.Amount, tx.Fee, reference, tx.Date,
                    order.Id, order.Status.ToString(), eshopAmount, order.Payment.ReferenceId, amountMismatch));
            }
            else
            {
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.MissingInEShop,
                    tx.TransactionId, tx.Status, tx.Amount, tx.Fee, reference, tx.Date,
                    null, null, null, null, false));
            }
        }

        // eShop orders whose payment PayPal's report does not show for this range.
        foreach (var order in orders.Where(o => o.Payment is not null))
        {
            var reference = order.Payment!.ReferenceId;
            if (matchedReferences.Contains(reference))
                continue;
            if (order.OrderDate < from || order.OrderDate > to)
                continue;

            var eshopAmount = order.Payment.CapturedAmount ?? order.Payment.AuthorizedAmount;
            lines.Add(new ReconciliationLine(
                ReconciliationMatch.MissingInPayPal,
                null, null, null, null, reference, null,
                order.Id, order.Status.ToString(), eshopAmount, reference, false));
        }

        var matched = lines.Count(l => l.Match == ReconciliationMatch.Matched);
        var missingInEShop = lines.Count(l => l.Match == ReconciliationMatch.MissingInEShop);
        var missingInPayPal = lines.Count(l => l.Match == ReconciliationMatch.MissingInPayPal);

        return new ReconciliationReport(
            from, to, DateTimeOffset.UtcNow,
            transactions.Count, matched, missingInEShop, missingInPayPal, lines);
    }
}
