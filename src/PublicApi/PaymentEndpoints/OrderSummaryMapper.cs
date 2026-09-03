using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps an <see cref="Order"/> aggregate to the read model returned by GET /api/my-orders.</summary>
public static class OrderSummaryMapper
{
    public static OrderSummaryDto Map(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        PaymentStatus = order.PaymentStatus.ToString(),
        Items = order.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = MapPayment(order.Payment)
    };

    private static PaymentDetailsDto? MapPayment(OrderPayment? payment)
    {
        if (payment is null)
            return null;

        return new PaymentDetailsDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            Currency = payment.CurrencyCode,
            Amount = payment.Amount,
            PaymentMethodId = payment.PaymentMethodId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedGross = payment.CapturedGross,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded(),
            Refunds = payment.Refunds.Select(r => new RefundDetailDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
