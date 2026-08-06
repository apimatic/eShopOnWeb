using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>An order plus its payment state, as returned to the caller.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Payment lifecycle: AwaitingPayment, Paid, or Refunded.</summary>
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentProvider { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }
    public DateTimeOffset? PaidDate { get; set; }
    public DateTimeOffset? RefundedDate { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.Currency,
        PaymentStatus = order.PaymentStatus.ToString(),
        PaymentProvider = order.PaymentProvider,
        CaptureId = order.PaymentCaptureId,
        RefundId = order.PaymentRefundId,
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

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
