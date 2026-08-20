using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Maps domain entities onto the view models returned by the API.</summary>
public static class PaymentMapping
{
    public static PaymentDetailsViewModel ToViewModel(Payment payment, Order order)
    {
        var items = order.OrderItems
            .Select(i => new OrderLineViewModel(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedDate)
            .Select(r => new RefundViewModel(r.PayPalRefundId, r.Amount, r.Status, r.CreatedDate))
            .ToList();

        return new PaymentDetailsViewModel(
            OrderId: payment.OrderId,
            OrderDate: order.OrderDate,
            BuyerId: payment.BuyerId,
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            Status: payment.Status.ToString(),
            PayPalOrderId: payment.PayPalOrderId,
            AuthorizationId: payment.AuthorizationId,
            AuthorizationStatus: payment.AuthorizationStatus,
            CaptureId: payment.CaptureId,
            CaptureStatus: payment.CaptureStatus,
            CapturedAmount: payment.CapturedAmount,
            PayPalFee: payment.PayPalFee,
            NetAmount: payment.NetAmount,
            TotalRefunded: payment.TotalRefunded(),
            RefundableRemaining: payment.RefundableRemaining(),
            LastError: payment.LastError,
            Refunds: refunds,
            Items: items);
    }

    public static PaymentMethodViewModel ToViewModel(PaymentMethod paymentMethod) =>
        new(paymentMethod.Id,
            paymentMethod.CardBrand,
            paymentMethod.LastFourDigits,
            paymentMethod.Expiry,
            paymentMethod.CardholderName,
            paymentMethod.CreatedDate);
}
