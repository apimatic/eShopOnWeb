using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethodDescription { get; set; } = string.Empty;
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps domain entities onto safe response DTOs (never exposing card data).</summary>
public static class PaymentDtoMapper
{
    public static OrderDto ToDto(this Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = order.Payment is null ? null : ToDto(order.Payment)
    };

    public static PaymentDto ToDto(this Payment payment) => new()
    {
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
        RefundableRemaining = payment.CaptureId is null ? 0m : payment.RefundableRemaining,
        Currency = payment.Currency,
        PaymentMethodDescription = payment.PaymentMethodDescription,
        Refunds = payment.Refunds.Select(ToDto).ToList()
    };

    public static RefundDto ToDto(this PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };

    public static PaymentMethodDto ToDto(this PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        Alias = method.Alias,
        CreatedAt = method.CreatedAt
    };
}
