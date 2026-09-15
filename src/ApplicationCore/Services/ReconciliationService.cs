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
    private readonly IPayPalGateway _payPalGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPalGateway, IReadRepository<Order> orderRepository)
    {
        _payPalGateway = payPalGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException(400, "`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPalGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersForReconciliationSpecification(from, to), cancellationToken);
        var eshopEntries = orders.Select(ToEntry).ToList();

        var unmatchedPaypal = new List<PayPalReportedTransaction>(paypalTransactions);
        var unmatchedEshop = new List<EshopReconciliationEntry>(eshopEntries);
        var matches = new List<ReconciliationMatch>();

        foreach (var paypal in paypalTransactions)
        {
            var eshop = unmatchedEshop.FirstOrDefault(e => Matches(paypal, e));
            if (eshop is null)
            {
                continue;
            }

            matches.Add(new ReconciliationMatch { PayPal = paypal, Eshop = eshop });
            unmatchedPaypal.Remove(paypal);
            unmatchedEshop.Remove(eshop);
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactions = paypalTransactions,
            Matches = matches,
            PayPalOnly = unmatchedPaypal,
            EshopOnly = unmatchedEshop
        };
    }

    private static EshopReconciliationEntry ToEntry(Order order)
    {
        return new EshopReconciliationEntry
        {
            OrderId = order.Id,
            Status = order.Status,
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            CaptureId = order.PayPalCaptureId,
            RefundIds = order.Refunds.Select(r => r.PayPalRefundId).ToList(),
            InvoiceId = $"ESHOP-{order.Id}-{order.OrderDate.UtcTicks}"
        };
    }

    private static bool Matches(PayPalReportedTransaction paypal, EshopReconciliationEntry eshop)
    {
        return IdEquals(paypal.TransactionId, eshop.CaptureId)
               || IdEquals(paypal.TransactionId, eshop.AuthorizationId)
               || IdEquals(paypal.TransactionId, eshop.PayPalOrderId)
               || eshop.RefundIds.Any(id => IdEquals(paypal.TransactionId, id))
               || IdEquals(paypal.ReferenceId, eshop.CaptureId)
               || IdEquals(paypal.ReferenceId, eshop.AuthorizationId)
               || IdEquals(paypal.ReferenceId, eshop.PayPalOrderId)
               || IdEquals(paypal.InvoiceId, eshop.InvoiceId)
               || IdEquals(paypal.CustomField, eshop.OrderId.ToString())
               || IdEquals(paypal.CustomField, eshop.InvoiceId);
    }

    private static bool IdEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
