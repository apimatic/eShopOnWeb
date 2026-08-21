using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>A single refund taken against a payment.</summary>
public class RefundDto
{
    public int Id { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>The payment state for an order, safe to return to a caller.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
}

/// <summary>A line on an order.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order paired with its payment state (for GET /api/my-orders).</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }
}

/// <summary>Maps payment domain entities onto the response DTOs.</summary>
public static class PaymentDtoMapper
{
    public static OrderPaymentDto ToDto(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        CurrencyCode = payment.CurrencyCode,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedGrossAmount = payment.CapturedGrossAmount,
        PayPalFee = payment.PayPalFeeAmount,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedDate)
            .Select(r => new RefundDto
            {
                Id = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                CurrencyCode = r.CurrencyCode,
                Status = r.Status,
                CreatedDate = r.CreatedDate
            })
            .ToList(),
        LastErrorMessage = payment.LastErrorMessage,
        CreatedDate = payment.CreatedDate,
        UpdatedDate = payment.UpdatedDate
    };

    public static MyOrderDto ToDto(MyOrderResult result)
    {
        var order = result.Order;
        return new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems
                .Select(i => new OrderLineDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                })
                .ToList(),
            Payment = result.Payment is null ? null : ToDto(result.Payment)
        };
    }
}
