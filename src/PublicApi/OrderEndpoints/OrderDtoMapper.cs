using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderDtoMapper
{
    public static OrderDto ToDto(this Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        PaymentStatus = order.PaymentStatus.ToString(),
        PaymentCaptureId = order.PaymentCaptureId,
        PaymentRefundId = order.PaymentRefundId,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}
