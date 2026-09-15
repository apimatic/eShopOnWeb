using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Safe, shopper-facing view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The state PayPal owns for this order — ids and statuses for the hold, capture and refunds.</summary>
public class PaymentDto
{
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
    public decimal? NetProceeds { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, string defaultCurrency)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Payment?.Currency ?? defaultCurrency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (order.Payment != null)
        {
            var p = order.Payment;
            dto.Payment = new PaymentDto
            {
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
                NetProceeds = p.NetAmount,
                TotalRefunded = p.TotalRefunded(),
                RemainingRefundable = p.RemainingRefundable(),
                CardBrand = p.CardBrand,
                CardLast4 = p.CardLast4,
                Refunds = p.Refunds.Select(r => new RefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }

        return dto;
    }
}
