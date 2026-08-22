using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public ReconciliationService(IRepository<Order> orderRepository, IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.CheckoutException("'to' must be on or after 'from'.", 400);
        }

        var paypal = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<ProviderTransaction>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in paypal)
        {
            var order = FindOrder(orders, tx);
            if (order is null)
            {
                paypalOnly.Add(tx);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            if (!string.IsNullOrEmpty(tx.TransactionId))
            {
                matchedTxIds.Add(tx.TransactionId);
            }

            matched.Add(new ReconciliationMatch(
                order.Id,
                tx.TransactionId,
                tx.InvoiceId,
                order.Status.ToString(),
                tx.Status,
                order.Total(),
                tx.Amount));
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalFootprint(o))
            .Select(o => new ReconciliationOrder(
                o.Id,
                o.Status.ToString(),
                o.Total(),
                o.PayPalOrderId,
                o.PayPalCaptureId,
                o.PayPalAuthorizationId))
            .ToList();

        var awaitingWithoutPaypal = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && !HasPayPalFootprint(o))
            .Select(o => new ReconciliationOrder(
                o.Id,
                o.Status.ToString(),
                o.Total(),
                o.PayPalOrderId,
                o.PayPalCaptureId,
                o.PayPalAuthorizationId));
        eshopOnly.AddRange(awaitingWithoutPaypal);

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private static bool HasPayPalFootprint(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId) ||
        !string.IsNullOrEmpty(order.PayPalAuthorizationId) ||
        !string.IsNullOrEmpty(order.PayPalCaptureId);

    private static Order? FindOrder(IReadOnlyList<Order> orders, ProviderTransaction tx)
    {
        if (int.TryParse(tx.InvoiceId, out var invoiceOrderId))
        {
            var byInvoice = orders.FirstOrDefault(o => o.Id == invoiceOrderId);
            if (byInvoice is not null)
            {
                return byInvoice;
            }
        }

        if (int.TryParse(tx.CustomField, out var customOrderId))
        {
            var byCustom = orders.FirstOrDefault(o => o.Id == customOrderId);
            if (byCustom is not null)
            {
                return byCustom;
            }
        }

        return orders.FirstOrDefault(o =>
            (!string.IsNullOrEmpty(tx.TransactionId) &&
             (string.Equals(o.PayPalCaptureId, tx.TransactionId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.PayPalAuthorizationId, tx.TransactionId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.PayPalOrderId, tx.TransactionId, StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrEmpty(tx.PaypalReferenceId) &&
             (string.Equals(o.PayPalCaptureId, tx.PaypalReferenceId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.PayPalAuthorizationId, tx.PaypalReferenceId, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(o.PayPalOrderId, tx.PaypalReferenceId, StringComparison.OrdinalIgnoreCase))));
    }
}
