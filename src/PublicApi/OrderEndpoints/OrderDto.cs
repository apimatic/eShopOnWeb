using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentDto
{
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public DateTimeOffset? AuthorizationTime { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class OrderDtoMapper
{
    public static OrderDto Map(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        Total = order.Total(),
        OrderDate = order.OrderDate,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList(),
        Payment = HasPayment(order) ? new PaymentDto
        {
            PaypalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizedAmount = order.AuthorizedAmount,
            AuthorizationTime = order.AuthorizationTime,
            AuthorizationExpirationTime = order.AuthorizationExpirationTime,
            CaptureId = order.CaptureId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount
        } : null,
        Refunds = order.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Status = r.Status,
            Amount = r.Amount,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    private static bool HasPayment(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId)
        || !string.IsNullOrEmpty(order.AuthorizationId)
        || !string.IsNullOrEmpty(order.CaptureId);
}
