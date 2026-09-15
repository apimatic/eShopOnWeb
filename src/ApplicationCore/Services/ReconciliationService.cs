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
    private readonly IPayPalPaymentGateway _gateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalPaymentGateway gateway)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's record over the whole range (chunked + fully paginated by the gateway).
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's own record for the same range.
        var orders = await _orderRepository.ListAsync(
            new OrdersWithPaymentByDateRangeSpecification(from, to), cancellationToken);

        // Index eShop orders by the reconciliation reference we stamped on their transactions and by
        // every PayPal id we hold, so a transaction can be matched either by its custom field or by a
        // PayPal id.
        var orderByReference = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var orderByPayPalId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var p = order.Payment!;
            Index(orderByReference, p.ReconciliationReference, order);
            Index(orderByPayPalId, p.PayPalOrderId, order);
            Index(orderByPayPalId, p.AuthorizationId, order);
            Index(orderByPayPalId, p.CaptureId, order);
            foreach (var refund in p.Refunds)
                Index(orderByPayPalId, refund.PayPalRefundId, order);
        }

        var matched = new List<ReconciledEntry>();
        var payPalOnly = new List<PayPalTransaction>();
        var seenOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = MatchOrder(txn, orderByReference, orderByPayPalId);
            if (order is not null)
            {
                seenOrderIds.Add(order.Id);
                matched.Add(new ReconciledEntry(order.Id, order.Payment!.State.ToString(), txn));
            }
            else
            {
                payPalOnly.Add(txn);
            }
        }

        var eShopOnly = orders
            .Where(o => !seenOrderIds.Contains(o.Id))
            .Select(o => new UnmatchedOrder(
                o.Id,
                o.Payment!.State.ToString(),
                o.Payment.Amount,
                o.Payment.Currency,
                o.Payment.AuthorizationId,
                o.Payment.CaptureId))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
    }

    private static Order? MatchOrder(PayPalTransaction txn, IReadOnlyDictionary<string, Order> orderByReference,
        IReadOnlyDictionary<string, Order> orderByPayPalId)
    {
        // Primary match: the custom field we stamp on every order == the stored reconciliation reference.
        if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
            orderByReference.TryGetValue(txn.CustomField!, out var byCustom))
        {
            return byCustom;
        }

        // Secondary match: the transaction id corresponds to a PayPal id we hold. PayPal's reporting
        // ids can carry a trailing suffix, so compare on a normalized prefix as well.
        if (!string.IsNullOrWhiteSpace(txn.TransactionId))
        {
            if (orderByPayPalId.TryGetValue(txn.TransactionId, out var byId))
                return byId;
            foreach (var kvp in orderByPayPalId)
            {
                if (txn.TransactionId!.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.StartsWith(txn.TransactionId, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }

        return null;
    }

    private static void Index(IDictionary<string, Order> map, string? id, Order order)
    {
        if (!string.IsNullOrWhiteSpace(id))
            map[id!] = order;
    }
}
