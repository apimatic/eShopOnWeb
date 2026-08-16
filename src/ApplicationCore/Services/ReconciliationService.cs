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

/// <summary>
/// Lines PayPal's own transaction records up against eShop's orders over a date range, so a payment PayPal
/// knows about but eShop doesn't — or the reverse — is visible. Covers the whole range (the gateway pages
/// through every page of PayPal's report).
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private const string Matched = "Matched";
    private const string MissingInEShop = "MissingInEShop";     // PayPal has it, eShop doesn't
    private const string MissingInPayPal = "MissingInPayPal";   // eShop has it, PayPal hasn't reported it (yet)

    private readonly IPayPalPaymentGateway _gateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.", nameof(to));
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(
            new OrdersWithPaymentInRangeSpecification(from, to), cancellationToken);

        // Every eShop money movement (capture + completed refunds) indexed by the id PayPal would report.
        var eshopById = new Dictionary<string, (int OrderId, decimal Amount, string Currency)>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(payment.CaptureId) && payment.CapturedAmount is decimal captured)
            {
                eshopById[payment.CaptureId!] = (order.Id, captured, payment.CurrencyCode);
            }

            foreach (var refund in payment.Refunds.Where(r => !string.IsNullOrEmpty(r.PayPalRefundId)))
            {
                eshopById[refund.PayPalRefundId!] = (order.Id, refund.Amount, refund.CurrencyCode);
            }
        }

        var lines = new List<ReconciliationLine>();
        var seenOnPayPal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            seenOnPayPal.Add(tx.TransactionId);
            var matched = eshopById.TryGetValue(tx.TransactionId, out var eshop);
            lines.Add(new ReconciliationLine(
                tx.TransactionId, tx.Amount, tx.CurrencyCode, tx.Status, tx.Date,
                matched ? eshop.OrderId : null,
                matched ? Matched : MissingInEShop));
        }

        // The reverse direction: money eShop recorded that PayPal's report doesn't (yet) show.
        foreach (var entry in eshopById.Where(e => !seenOnPayPal.Contains(e.Key)))
        {
            var (id, (orderId, amount, currency)) = entry;
            lines.Add(new ReconciliationLine(
                id, amount, currency, PayPalStatus: "NOT_REPORTED", Date: default,
                OrderId: orderId, MatchState: MissingInPayPal));
        }

        return new ReconciliationReport(from, to, lines);
    }
}
