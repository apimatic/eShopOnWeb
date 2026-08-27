using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequestDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>Full card details for a one-off payment. Never stored, never logged.</summary>
public class CardDetailsDto
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToModel() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToModel());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "";

    public BillingAddress ToModel() => new(AddressLine1, AddressLine2, AdminArea2, AdminArea1, PostalCode, CountryCode);
}

public class RefundDto
{
    public string RefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = "";
    public int? SavedCardId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromEntity(OrderPayment payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpirationTime = payment.AuthorizationExpirationTime,
        AuthorizedAmount = payment.AuthorizedAmount,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        Currency = payment.Currency,
        SavedCardId = payment.SavedCardId,
        TotalRefunded = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            IdempotencyKey = r.IdempotencyKey,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

internal static class OrderMapping
{
    public static OrderDto ToDto(Order order, OrderPayment? payment) => new()
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
        Payment = payment == null ? null : PaymentDto.FromEntity(payment)
    };
}

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The shopper's identity, taken from the JWT.</summary>
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The token carries no usable identity claim.");
        }
        return name;
    }
}
