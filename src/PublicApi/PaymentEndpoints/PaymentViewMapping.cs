using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps order/payment domain entities to the API response shapes.</summary>
internal static class PaymentViewMapping
{
    public static PaymentStateDto? ToPaymentState(Payment? payment)
    {
        if (payment is null) return null;

        return new PaymentStateDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            AuthorizedAmount = payment.AuthorizedAmount,
            CaptureId = payment.CaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded(),
            Refunds = payment.Refunds
                .Select(r => new RefundStateDto { RefundId = r.RefundId, Amount = r.Amount, Status = r.Status })
                .ToList()
        };
    }

    public static OrderSummaryDto ToOrderSummary(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.Payment?.Currency ?? string.Empty,
        Items = order.OrderItems
            .Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            })
            .ToList(),
        Payment = ToPaymentState(order.Payment)
    };
}
