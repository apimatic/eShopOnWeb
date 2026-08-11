using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("'to' must be on or after 'from'.");
        }

        // PayPal's own record for the range (all pages).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop orders that reference a PayPal money movement (owned payment loads with the order).
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var capturedOrders = orders.Where(o => o.Payment?.CaptureId is not null).ToList();

        // Map every PayPal id an eShop order knows about back to its order.
        var eShopIdToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null) continue;
            AddId(eShopIdToOrder, payment.PayPalOrderId, order.Id);
            AddId(eShopIdToOrder, payment.AuthorizationId, order.Id);
            AddId(eShopIdToOrder, payment.CaptureId, order.Id);
            foreach (var refund in payment.Refunds)
            {
                AddId(eShopIdToOrder, refund.RefundId, order.Id);
            }
        }

        var matched = new List<ReconciliationMatch>();
        var onlyInPayPal = new List<PayPalTransaction>();
        var payPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            if (!string.IsNullOrEmpty(tx.TransactionId))
            {
                payPalIds.Add(tx.TransactionId);
            }

            if (!string.IsNullOrEmpty(tx.TransactionId) && eShopIdToOrder.TryGetValue(tx.TransactionId, out var orderId))
            {
                matched.Add(new ReconciliationMatch(tx.TransactionId, orderId, tx.Status, tx.Amount, tx.CurrencyCode));
            }
            else
            {
                onlyInPayPal.Add(tx);
            }
        }

        var onlyInEShop = capturedOrders
            .Where(o => !payPalIds.Contains(o.Payment!.CaptureId!))
            .Select(o => new EShopCapturedPayment(o.Id, o.Payment!.CaptureId!, o.Payment.CapturedAmount, o.Payment.Currency))
            .ToList();

        return new ReconciliationResult(from, to, transactions.Count, capturedOrders.Count,
            matched, onlyInPayPal, onlyInEShop);
    }

    private static void AddId(IDictionary<string, int> map, string? id, int orderId)
    {
        if (!string.IsNullOrEmpty(id))
        {
            map[id] = orderId;
        }
    }
}
