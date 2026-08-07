using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class OrderMapping
{
    public const string Currency = "USD";

    /// <summary>The caller's identity, taken from the token's name claim. Used as the buyer id.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user)
        => user.Identity?.Name;

    public static OrderItemDto[] ToItemDtos(this Order order)
        => order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToArray();

    public static MyOrderDto ToMyOrderDto(this Order order)
        => new()
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = Currency,
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            CaptureId = order.PayPalCaptureId,
            RefundId = order.PayPalRefundId,
            Items = order.ToItemDtos().ToList()
        };
}
