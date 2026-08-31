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
/// Lines up the payment gateway's own record of transactions against eShop orders,
/// so a payment known on only one side is visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var gatewayTransactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        // Index every gateway-side identifier eShop knows about.
        var knownIds = new Dictionary<string, (Order Order, Payment Payment)>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            void Index(string? id)
            {
                if (!string.IsNullOrEmpty(id) && !knownIds.ContainsKey(id))
                {
                    knownIds[id] = (order, payment);
                }
            }
            Index(payment.PayPalOrderId);
            Index(payment.AuthorizationId);
            Index(payment.CaptureId);
            foreach (var refund in payment.Refunds)
            {
                Index(refund.PayPalRefundId);
            }
        }

        var report = new ReconciliationReport { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in gatewayTransactions)
        {
            var entry = new ReconciliationEntry
            {
                GatewayTransactionId = txn.TransactionId,
                GatewayReferenceId = txn.ReferenceId,
                GatewayEventCode = txn.EventCode,
                GatewayDate = txn.InitiationDate,
                GatewayAmount = txn.Amount,
                GatewayFee = txn.Fee,
                GatewayStatus = txn.Status,
                Currency = txn.Currency
            };

            if ((txn.TransactionId is not null && knownIds.TryGetValue(txn.TransactionId, out var byTxn)) ||
                (txn.ReferenceId is not null && knownIds.TryGetValue(txn.ReferenceId, out byTxn)))
            {
                entry.MatchStatus = "Matched";
                entry.OrderId = byTxn.Order.Id;
                entry.PaymentId = byTxn.Payment.Id;
                entry.ShopPaymentStatus = byTxn.Payment.Status.ToString();
                entry.ShopAmount = byTxn.Payment.CapturedAmount ?? byTxn.Payment.Amount;
                entry.Currency ??= byTxn.Payment.Currency;
                matchedPaymentIds.Add(byTxn.Payment.Id);
            }
            else
            {
                entry.MatchStatus = "OnlyInGateway";
                report.OnlyInGatewayCount++;
            }
            report.Entries.Add(entry);
        }

        // Payments eShop recorded in the range that the gateway report does not list.
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (matchedPaymentIds.Contains(payment.Id))
            {
                continue;
            }
            var relevant = payment.CapturedAt ?? payment.CreatedAt;
            if (relevant < from || relevant > to)
            {
                continue;
            }
            report.Entries.Add(new ReconciliationEntry
            {
                MatchStatus = "OnlyInShop",
                OrderId = order.Id,
                PaymentId = payment.Id,
                ShopPaymentStatus = payment.Status.ToString(),
                ShopAmount = payment.CapturedAmount ?? payment.Amount,
                Currency = payment.Currency,
                GatewayTransactionId = payment.CaptureId ?? payment.AuthorizationId
            });
            report.OnlyInShopCount++;
        }

        report.MatchedCount = report.Entries.Count(e => e.MatchStatus == "Matched");
        report.Entries = report.Entries
            .OrderBy(e => e.GatewayDate ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.OrderId)
            .ToList();
        return report;
    }
}
