using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderDtoMapper
{
    public static OrderDto ToDto(this Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = "USD",
        PaymentStatus = order.PaymentStatus.ToString(),
        PaymentCardDescription = order.PaymentCardDescription,
        PayPalOrderId = order.PayPalOrderId,
        PayPalCaptureId = order.PayPalCaptureId,
        PayPalRefundId = order.PayPalRefundId,
        PaidDate = order.PaidDate,
        RefundedDate = order.RefundedDate,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}
