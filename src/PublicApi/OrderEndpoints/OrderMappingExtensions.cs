using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderMappingExtensions
{
    public static OrderDto ToDto(this Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = "USD",
        PaymentStatus = order.PaymentStatus.ToString(),
        PaidAt = order.PaidAt,
        RefundedAt = order.RefundedAt,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}
