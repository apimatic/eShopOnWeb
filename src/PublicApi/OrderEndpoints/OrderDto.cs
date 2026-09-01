using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An order with its full payment state as known to both eShop and PayPal.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto FromOrder(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            CaptureId = order.CaptureId,
            CapturedAmount = order.CapturedGrossAmount,
            PayPalFee = order.CapturedFeeAmount,
            NetAmount = order.CapturedNetAmount,
            TotalRefunded = order.TotalRefunded,
            RemainingRefundable = order.PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded
                ? order.RemainingRefundable
                : 0m,
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Currency = r.Currency,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
