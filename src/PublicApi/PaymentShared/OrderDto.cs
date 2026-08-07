using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>A shopper-facing view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Payment lifecycle: <c>AwaitingPayment</c>, <c>Paid</c> or <c>Refunded</c>.</summary>
    public string PaymentStatus { get; set; } = nameof(OrderPaymentStatus.AwaitingPayment);

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset? PaidDate { get; set; }
    public DateTimeOffset? RefundedDate { get; set; }

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
