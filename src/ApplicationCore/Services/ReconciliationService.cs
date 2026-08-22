using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "Query parameter 'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivitySpecification(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in paypalTransactions)
        {
            var order = orders.FirstOrDefault(o => Matches(o, transaction));
            if (order is null)
            {
                continue;
            }

            matched.Add(new ReconciliationMatch { OrderId = order.Id, Transaction = transaction });
            matchedTransactionIds.Add(transaction.TransactionId);
            matchedOrderIds.Add(order.Id);
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedTransactionIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaymentIdentifiers(o) && OverlapsRange(o, from, to))
            .Select(ToSummary)
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static bool Matches(Order order, PayPalTransactionRecord transaction)
    {
        var identifiers = CollectIdentifiers(order);
        if (identifiers.Contains(transaction.TransactionId)
            || (!string.IsNullOrEmpty(transaction.PaypalReferenceId) && identifiers.Contains(transaction.PaypalReferenceId)))
        {
            return true;
        }

        if (string.IsNullOrEmpty(transaction.InvoiceId) || string.IsNullOrEmpty(order.PayPalCreateRequestId))
        {
            return false;
        }

        return string.Equals(transaction.InvoiceId, $"ESHOP-{order.Id}-AUTH-{order.PayPalCreateRequestId}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transaction.InvoiceId, $"ESHOP-{order.Id}-CAP-{order.PayPalCreateRequestId}", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> CollectIdentifiers(Order order)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, order.PayPalOrderId);
        Add(ids, order.PayPalAuthorizationId);
        Add(ids, order.PayPalOriginalAuthorizationId);
        Add(ids, order.PayPalCaptureId);
        foreach (var refund in order.Refunds)
        {
            Add(ids, refund.PayPalRefundId);
        }

        return ids;
    }

    private static void Add(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static bool HasPaymentIdentifiers(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId)
        || !string.IsNullOrEmpty(order.PayPalAuthorizationId)
        || !string.IsNullOrEmpty(order.PayPalCaptureId)
        || order.Refunds.Count > 0;

    private static bool OverlapsRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        bool InRange(DateTimeOffset? value) => value.HasValue && value.Value >= from && value.Value <= to;
        return InRange(order.OrderDate)
            || InRange(order.OriginalAuthorizedAt)
            || InRange(order.CapturedAt)
            || order.Refunds.Any(r => InRange(r.CreatedAt));
    }

    private static EshopPaymentSummary ToSummary(Order order) =>
        new()
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            CaptureId = order.PayPalCaptureId,
            RefundIds = order.Refunds.Select(r => r.PayPalRefundId).ToArray()
        };
}
