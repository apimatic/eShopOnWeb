using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPaymentGateway paymentGateway)
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
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        var paypal = await _paymentGateway.SearchTransactionsAsync(from, to, cts.Token);
        var orders = await _orderRepository.ListAsync(new OrdersInRangeSpecification(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<GatewayTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypal)
        {
            var order = FindOrder(orders, txn);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matched.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                CaptureId = order.CaptureId,
                AuthorizationId = order.AuthorizationId,
                Paypal = txn
            });
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && o.Status != OrderPaymentStatus.AwaitingPayment)
            .Select(o => new EshopOrphan
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                CaptureId = o.CaptureId,
                AuthorizationId = o.AuthorizationId,
                Amount = o.CapturedAmount ?? o.Total()
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PaypalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static Order? FindOrder(IReadOnlyList<Order> orders, GatewayTransaction txn)
    {
        if (!string.IsNullOrEmpty(txn.InvoiceId))
        {
            var suffix = txn.InvoiceId.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase)
                ? txn.InvoiceId["ESHOP-".Length..]
                : txn.InvoiceId;
            if (int.TryParse(suffix, out var invoiceOrderId))
            {
                var byInvoice = orders.FirstOrDefault(o => o.Id == invoiceOrderId);
                if (byInvoice is not null)
                {
                    return byInvoice;
                }
            }
        }

        if (!string.IsNullOrEmpty(txn.CustomField) && int.TryParse(txn.CustomField, out var customOrderId))
        {
            var byCustom = orders.FirstOrDefault(o => o.Id == customOrderId);
            if (byCustom is not null)
            {
                return byCustom;
            }
        }

        if (!string.IsNullOrEmpty(txn.TransactionId) || !string.IsNullOrEmpty(txn.ReferenceId))
        {
            return orders.FirstOrDefault(o =>
                o.CaptureId == txn.TransactionId
                || o.AuthorizationId == txn.TransactionId
                || o.PaypalOrderId == txn.TransactionId
                || o.CaptureId == txn.ReferenceId
                || o.AuthorizationId == txn.ReferenceId
                || o.Refunds.Any(r => r.RefundId == txn.TransactionId));
        }

        return null;
    }
}
