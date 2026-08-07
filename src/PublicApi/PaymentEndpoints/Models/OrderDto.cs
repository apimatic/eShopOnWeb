using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;

/// <summary>An order and its payment state, safe to return to the shopper.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>AwaitingPayment, Paid or Refunded.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto FromOrder(Order order) => new OrderDto
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        PaymentStatus = order.PaymentStatus.ToString(),
        Total = order.Total(),
        Currency = "USD",
        PayPalOrderId = order.PayPalOrderId,
        CaptureId = order.PayPalCaptureId,
        RefundId = order.PayPalRefundId,
        Items = order.OrderItems.Select(OrderItemDto.FromOrderItem).ToList()
    };
}

/// <summary>A single line of an order.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderItemDto FromOrderItem(OrderItem item) => new OrderItemDto
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };
}
