using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderDtoMapper
{
    public static OrderDto ToDto(this Order order)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (order.Payment is not null)
        {
            var payment = order.Payment;
            dto.Payment = new OrderPaymentDto
            {
                Status = payment.Status.ToString(),
                CurrencyCode = payment.CurrencyCode,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
                CaptureId = payment.CaptureId,
                CaptureStatus = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount,
                PayPalFeeAmount = payment.PayPalFeeAmount,
                NetAmount = payment.NetAmount,
                TotalRefundedAmount = payment.TotalRefundedAmount,
                Refunds = payment.Refunds.Select(r => new OrderRefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }

        return dto;
    }
}
