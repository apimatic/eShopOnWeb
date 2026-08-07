using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A read model for an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Payment state: AwaitingPayment, Paid, or Refunded.</summary>
    public string PaymentStatus { get; set; } = nameof(ApplicationCore.Entities.OrderAggregate.PaymentStatus.AwaitingPayment);

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto FromOrder(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = "USD",
        PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        PayPalCaptureId = order.PayPalCaptureId,
        PayPalRefundId = order.PayPalRefundId,
        Items = order.OrderItems.Select(oi => new OrderItemDto
        {
            CatalogItemId = oi.ItemOrdered.CatalogItemId,
            ProductName = oi.ItemOrdered.ProductName,
            UnitPrice = oi.UnitPrice,
            Units = oi.Units
        }).ToList()
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
