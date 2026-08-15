using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>The payment state of an order, as far as PayPal and eShop know it.</summary>
public class PaymentDto
{
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string PayPalOrderId { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>A full view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public static class OrderPresentation
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
            }).ToList(),
            Payment = order.Payment is null ? null : ToDto(order.Payment),
        };
    }

    public static PaymentDto ToDto(Payment payment)
    {
        return new PaymentDto
        {
            Reference = payment.Reference,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedGross = payment.CapturedGross,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                Currency = r.Currency,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
            }).ToList(),
        };
    }
}
