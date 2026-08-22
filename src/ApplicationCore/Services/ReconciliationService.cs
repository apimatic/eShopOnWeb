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
    private readonly IRepository<Order> _orders;
    private readonly IPaymentGateway _payments;
    private readonly IPaymentSettings _settings;

    public ReconciliationService(
        IRepository<Order> orders,
        IPaymentGateway payments,
        IPaymentSettings settings)
    {
        _orders = orders;
        _payments = payments;
        _settings = settings;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var paypalRows = await _payments.SearchTransactionsAsync(from, to, ct);
        var localOrders = await _orders.ListAsync(new OrdersWithPayPalPaymentSpecification(), ct);
        var localByKey = IndexOrders(localOrders);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalTransactionRecord>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var row in paypalRows)
        {
            if (TryMatch(row, localByKey, out var order))
            {
                matched.Add(new ReconciliationMatch { PayPal = row, Order = Map(order) });
                matchedOrderIds.Add(order.Id);
            }
            else
            {
                paypalOnly.Add(row);
            }
        }

        var eshopOnly = localOrders
            .Where(o => !matchedOrderIds.Contains(o.Id) && InRange(o, from, to))
            .Select(Map)
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

    private static Dictionary<string, Order> IndexOrders(IEnumerable<Order> orders)
    {
        var index = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            index[$"id:{order.Id}"] = order;
            index[$"invoice:ESHOP-{order.Id}"] = order;
            if (!string.IsNullOrWhiteSpace(order.Payment.InvoiceId))
            {
                index[$"invoice:{order.Payment.InvoiceId}"] = order;
            }
            if (!string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId))
            {
                index[$"paypal:{order.Payment.PayPalOrderId}"] = order;
            }
            if (!string.IsNullOrWhiteSpace(order.Payment.CaptureId))
            {
                index[$"capture:{order.Payment.CaptureId}"] = order;
            }
            if (!string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
            {
                index[$"auth:{order.Payment.AuthorizationId}"] = order;
            }
        }

        return index;
    }

    private static bool TryMatch(PayPalTransactionRecord row, IReadOnlyDictionary<string, Order> index, out Order order)
    {
        if (!string.IsNullOrWhiteSpace(row.InvoiceId) && index.TryGetValue($"invoice:{row.InvoiceId}", out order!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(row.CustomField)
            && index.TryGetValue($"id:{row.CustomField}", out order!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(row.PaypalReferenceId)
            && index.TryGetValue($"paypal:{row.PaypalReferenceId}", out order!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(row.TransactionId))
        {
            if (index.TryGetValue($"capture:{row.TransactionId}", out order!))
            {
                return true;
            }

            if (index.TryGetValue($"auth:{row.TransactionId}", out order!))
            {
                return true;
            }
        }

        order = null!;
        return false;
    }

    private static bool InRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = order.Payment.OriginalAuthorizationTime ?? order.OrderDate;
        return stamp >= from && stamp <= to;
    }

    private ShopOrderResult Map(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Payment.Currency ?? _settings.Currency,
        OrderDate = order.OrderDate,
        PayPalOrderId = order.Payment.PayPalOrderId,
        AuthorizationId = order.Payment.AuthorizationId,
        AuthorizationStatus = order.Payment.AuthorizationStatus,
        AuthorizationExpiration = order.Payment.AuthorizationExpiration,
        CaptureId = order.Payment.CaptureId,
        CaptureStatus = order.Payment.CaptureStatus,
        CapturedAmount = order.Payment.CapturedAmount,
        PaypalFee = order.Payment.PaypalFee,
        NetAmount = order.Payment.NetAmount,
        RemainingRefundable = order.RemainingRefundable()
    };
}
