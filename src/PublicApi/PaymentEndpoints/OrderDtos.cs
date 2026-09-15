using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe, shopper-facing view of an order's payment state.</summary>
public class OrderPaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto? From(Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        var dto = new OrderPaymentDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            Currency = payment.Currency,
            AuthorizedAmount = payment.AuthorizedAmount,
            CardBrand = payment.CardBrand,
            CardLast4 = payment.CardLast4,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining
        };
        foreach (var refund in payment.Refunds)
        {
            dto.Refunds.Add(new OrderRefundDto
            {
                RefundId = refund.PayPalRefundId,
                Status = refund.Status,
                Amount = refund.Amount,
                CreatedAt = refund.CreatedAt
            });
        }
        return dto;
    }
}

public class OrderRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

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
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }

    public static OrderDto From(Order order)
    {
        var dto = new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Payment = OrderPaymentDto.From(order.Payment)
        };
        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }
        return dto;
    }
}
