using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>An order together with its payment state, as returned by the order endpoints.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderPaymentDto From(Order order, string fallbackCurrency)
    {
        var currency = order.Payment?.Currency ?? fallbackCurrency;
        return new OrderPaymentDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : PaymentDto.From(order.Payment)
        };
    }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The payment-processor state eShop holds for an order: the hold, the capture, refunds.</summary>
public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;

    public string AuthorizationId { get; set; } = string.Empty;
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

    public static PaymentDto From(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        InvoiceId = payment.InvoiceId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedGross = payment.CapturedGross,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds.Select(RefundDto.From).ToList()
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(Refund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };
}
