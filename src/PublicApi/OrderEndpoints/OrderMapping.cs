using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderMapping
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? "",
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : new OrderPaymentDto
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpiresAt = order.Payment.AuthorizationExpiresAt,
                ReauthorizationCount = order.Payment.ReauthorizationCount,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PayPalFeeAmount = order.Payment.PayPalFeeAmount,
                NetAmount = order.Payment.NetAmount,
                TotalRefunded = order.Payment.TotalRefunded,
                Refunds = order.Payment.Refunds.Select(r => new OrderRefundDto
                {
                    RefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    Note = r.Note,
                    CreatedAt = r.CreatedAt
                }).ToList()
            }
        };
    }
}
