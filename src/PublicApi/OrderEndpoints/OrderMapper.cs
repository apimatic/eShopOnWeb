using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderMapper
{
    public static OrderDto ToDto(this Order order, Payment? payment)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payment?.ToDto()
        };
    }

    public static PaymentDto ToDto(this Payment payment)
    {
        return new PaymentDto
        {
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PaymentMethodId = payment.PaymentMethodId,
            PayPalOrderId = payment.PayPalOrderId,
            PayPalAuthorizationId = payment.PayPalAuthorizationId,
            AuthorizationStatus = payment.PayPalAuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            PayPalCaptureId = payment.PayPalCaptureId,
            CaptureStatus = payment.PayPalCaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFeeAmount = payment.PayPalFeeAmount,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RemainingRefundable = payment.RemainingRefundable,
            Refunds = payment.Refunds.Select(r => r.ToDto()).ToList()
        };
    }

    public static RefundDto ToDto(this Refund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.PayPalStatus,
            CreatedAt = refund.CreatedAt
        };
    }
}
