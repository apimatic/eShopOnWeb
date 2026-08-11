using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>The payment state of an order, including everything PayPal owns (hold, capture, refunds).</summary>
public class PaymentDto
{
    public string Status { get; set; } = nameof(PaymentStatus.AwaitingPayment);
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal? RefundedAmount { get; set; }
    public decimal? RefundableRemaining { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
    public bool UsedSavedCard { get; set; }

    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto? FromEntity(Payment? p)
    {
        if (p is null) return null;
        return new PaymentDto
        {
            Status = p.Status.ToString(),
            Amount = p.Amount,
            Currency = p.Currency,
            PayPalOrderId = p.PayPalOrderId,
            AuthorizationId = p.AuthorizationId,
            AuthorizationStatus = p.AuthorizationStatus,
            AuthorizationExpiresAt = p.AuthorizationExpiresAt,
            CaptureId = p.CaptureId,
            CaptureStatus = p.CaptureStatus,
            CapturedAmount = p.CapturedAmount,
            PayPalFee = p.PayPalFee,
            NetAmount = p.NetAmount,
            RefundedAmount = p.CaptureId is null ? null : p.TotalRefunded,
            RefundableRemaining = p.CaptureId is null ? null : p.RefundableRemaining,
            CardBrand = p.CardBrand,
            CardLastDigits = p.CardLastDigits,
            UsedSavedCard = p.UsedSavedCard,
            Refunds = p.Refunds.Select(RefundDto.FromEntity).ToList()
        };
    }
}

/// <summary>A single refund against an order's capture.</summary>
public class RefundDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto FromEntity(PaymentRefund r) => new()
    {
        RefundId = r.Id,
        PayPalRefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Currency = r.Currency,
        Status = r.Status,
        IdempotencyKey = r.IdempotencyKey,
        CreatedAt = r.CreatedAt
    };
}

/// <summary>An order with its items and payment state.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = nameof(ApplicationCore.Entities.OrderAggregate.PaymentStatus.AwaitingPayment);
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderSummaryDto FromEntity(Order o) => new()
    {
        OrderId = o.Id,
        OrderDate = o.OrderDate,
        BuyerId = o.BuyerId,
        Total = o.Total(),
        PaymentStatus = o.PaymentStatus.ToString(),
        Items = o.OrderItems.Select(OrderItemDto.FromEntity).ToList(),
        Payment = PaymentDto.FromEntity(o.Payment)
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }

    public static OrderItemDto FromEntity(OrderItem i) => new()
    {
        CatalogItemId = i.ItemOrdered.CatalogItemId,
        ProductName = i.ItemOrdered.ProductName,
        UnitPrice = i.UnitPrice,
        Units = i.Units
    };
}
