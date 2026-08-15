using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class PaymentViewMapper
{
    public static OrderPaymentView ToView(Order order, Payment? payment)
    {
        var items = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new OrderPaymentView(
            order.Id,
            order.Status.ToString(),
            order.Total(),
            payment?.CurrencyCode ?? string.Empty,
            order.OrderDate,
            items,
            payment is null ? null : ToView(payment));
    }

    public static PaymentView ToView(Payment payment)
    {
        var refunds = payment.Refunds
            .Select(r => new RefundView(r.Id, r.RefundId, r.Amount, r.Status))
            .ToList();

        return new PaymentView(
            payment.Status.ToString(),
            payment.InstrumentDescription,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.TotalRefunded,
            payment.RefundableRemaining,
            refunds);
    }
}
