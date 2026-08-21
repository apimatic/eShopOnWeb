using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IReadRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentException(400, "Query parameter 'to' must be on or after 'from'.");
        }

        var paypalRows = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id.ToString(CultureInfo.InvariantCulture), o => o);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalRows)
        {
            var key = FirstNonEmpty(txn.InvoiceId, txn.CustomField);
            if (key is not null && ordersById.TryGetValue(key, out var order))
            {
                matchedOrderIds.Add(order.Id);
                matched.Add(new ReconciliationMatch(
                    order.Id,
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.CustomField,
                    txn.Status,
                    txn.Amount,
                    order.Total(),
                    order.PaymentStatus.ToString()));
            }
            else
            {
                paypalOnly.Add(new ReconciliationPayPalOnly(
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.CustomField,
                    txn.Status,
                    txn.Amount,
                    txn.FeeAmount));
            }
        }

        var eshopOnly = orders
            .Where(o => o.Payment is not null && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEshopOnly(
                o.Id,
                o.Payment!.PayPalOrderId,
                o.Payment.AuthorizationId,
                o.Payment.CaptureId,
                o.Total(),
                o.PaymentStatus.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
