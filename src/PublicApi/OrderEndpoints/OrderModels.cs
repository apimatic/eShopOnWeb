using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Optional shipping address for an order. Defaults are used when omitted.</summary>
public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Read model of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public List<OrderItemDto> Items { get; set; } = new();
    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }

    public static OrderDto FromOrder(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        PaymentStatus = order.PaymentStatus.ToString(),
        Total = order.Total(),
        Currency = "USD",
        Items = order.OrderItems.Select(oi => new OrderItemDto
        {
            CatalogItemId = oi.ItemOrdered.CatalogItemId,
            ProductName = oi.ItemOrdered.ProductName,
            UnitPrice = oi.UnitPrice,
            Units = oi.Units,
        }).ToList(),
        PayPalOrderId = order.PayPalOrderId,
        PayPalCaptureId = order.PayPalCaptureId,
        PayPalRefundId = order.PayPalRefundId,
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
