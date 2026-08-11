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
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalPaymentGateway gateway, IReadRepository<Order> orderRepository)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // PayPal's own record across the whole range (all pages).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's captured payments in the range.
        var orders = await _orderRepository.ListAsync(
            new OrdersWithCapturedPaymentSpecification(from, to), cancellationToken);

        var entries = new List<ReconciliationEntry>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            var payment = order.Payment!;
            var captureId = payment.CaptureId!;
            var expectedCustomField = $"order-{order.Id}";

            var match = transactions.FirstOrDefault(t =>
                string.Equals(t.TransactionId, captureId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.CustomField, expectedCustomField, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                matchedTransactionIds.Add(match.TransactionId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.Matched,
                    OrderId: order.Id,
                    PayPalTransactionId: match.TransactionId,
                    CaptureId: captureId,
                    EShopAmount: payment.CapturedGross,
                    PayPalAmount: match.Amount,
                    Currency: match.Currency ?? payment.Currency,
                    OrderStatus: order.Status.ToString(),
                    PayPalStatus: match.Status));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.InEShopOnly,
                    OrderId: order.Id,
                    PayPalTransactionId: null,
                    CaptureId: captureId,
                    EShopAmount: payment.CapturedGross,
                    PayPalAmount: null,
                    Currency: payment.Currency,
                    OrderStatus: order.Status.ToString(),
                    PayPalStatus: null));
            }
        }

        foreach (var transaction in transactions.Where(t => !matchedTransactionIds.Contains(t.TransactionId)))
        {
            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.InPayPalOnly,
                OrderId: null,
                PayPalTransactionId: transaction.TransactionId,
                CaptureId: null,
                EShopAmount: null,
                PayPalAmount: transaction.Amount,
                Currency: transaction.Currency,
                OrderStatus: null,
                PayPalStatus: transaction.Status));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            EShopCapturedPaymentCount: orders.Count,
            MatchedCount: entries.Count(e => e.Match == ReconciliationMatch.Matched),
            InPayPalOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.InPayPalOnly),
            InEShopOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.InEShopOnly),
            Entries: entries);
    }
}
