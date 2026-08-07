using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Currency used across the ordering flow.</summary>
public static class OrderCurrency
{
    public const string Code = "USD";
}

/// <summary>A single line on an order, safe to return to the caller.</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Full view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = OrderCurrency.Code;

    /// <summary>PayPal order id that captured payment, if paid.</summary>
    public string? PayPalOrderId { get; set; }

    /// <summary>PayPal capture id, if paid.</summary>
    public string? CaptureId { get; set; }

    /// <summary>PayPal refund id, if refunded.</summary>
    public string? RefundId { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto FromEntity(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        PaymentStatus = order.PaymentStatus.ToString(),
        Total = order.Total(),
        Currency = OrderCurrency.Code,
        PayPalOrderId = order.PaymentProviderOrderId,
        CaptureId = order.PaymentCaptureId,
        RefundId = order.PaymentRefundId,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}
