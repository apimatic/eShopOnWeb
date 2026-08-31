using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}

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
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
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
    public decimal RefundableAmount { get; set; }
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

internal static class OrderMapping
{
    public static string? GetUserName(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;

    public static PaymentDto ToDto(OrderPayment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundableAmount = payment.RefundableAmount,
        Refunds = MapRefunds(payment)
    };

    public static List<RefundDto> MapRefunds(OrderPayment payment)
    {
        var refunds = new List<RefundDto>();
        foreach (var refund in payment.Refunds)
        {
            refunds.Add(new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.PayPalStatus,
                IdempotencyKey = refund.IdempotencyKey,
                CreatedAt = refund.CreatedAt
            });
        }
        return refunds;
    }

    public static OrderDto ToDto(Order order, OrderPayment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = MapItems(order),
        Payment = payment == null ? null : ToDto(payment)
    };

    public static List<OrderItemDto> MapItems(Order order)
    {
        var items = new List<OrderItemDto>();
        foreach (var item in order.OrderItems)
        {
            items.Add(new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }
        return items;
    }
}
