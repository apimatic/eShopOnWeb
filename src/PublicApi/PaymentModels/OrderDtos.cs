using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A shopper-facing view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The PayPal-owned state of an order's payment, safe to show the shopper.</summary>
public class OrderPaymentDto
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? MerchantReference { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? SavedCardDescriptor { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A saved card, described safely enough to recognise but never revealing full details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? Expiry { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps domain aggregates to their wire DTOs.</summary>
public static class PaymentMappings
{
    public static OrderDto ToDto(this Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Currency = order.Payment?.Currency ?? string.Empty,
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = order.Payment?.ToDto()
    };

    public static OrderPaymentDto ToDto(this OrderPayment payment) => new()
    {
        Amount = payment.Amount,
        Currency = payment.Currency,
        MerchantReference = payment.MerchantReference,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedGross = payment.CapturedGross,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        CardBrand = payment.CardBrand,
        CardLast4 = payment.CardLast4,
        SavedCardDescriptor = payment.SavedPaymentMethodDescriptor,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
    };

    public static PaymentMethodDto ToDto(this SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        CardBrand = method.CardBrand,
        CardLast4 = method.CardLast4,
        Expiry = method.CardExpiry,
        Label = method.Label,
        CreatedAt = method.CreatedAt
    };
}
