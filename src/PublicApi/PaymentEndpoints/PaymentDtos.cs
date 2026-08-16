using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by the caller for a one-off payment or to be vaulted. Never persisted or logged.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        ExpiryMonth = ExpiryMonth,
        ExpiryYear = ExpiryYear,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingLine1 = BillingLine1,
        BillingLine2 = BillingLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingCountryCode = BillingCountryCode,
        BillingPostalCode = BillingPostalCode
    };
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State ?? string.Empty, Country, ZipCode);
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
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Payment state as PayPal owns it, safe to show the shopper (no card details).</summary>
public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
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

/// <summary>A saved card described safely enough to recognise it — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps domain entities onto the safe response shapes.</summary>
public static class PaymentMappings
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

    public static PaymentDto ToDto(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            Id = r.Id,
            Amount = r.Amount,
            PayPalRefundId = r.PayPalRefundId,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    public static PaymentMethodDto ToDto(this PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Alias = paymentMethod.Alias,
        CardBrand = paymentMethod.CardBrand,
        Last4 = paymentMethod.Last4,
        ExpiryMonth = paymentMethod.ExpiryMonth,
        ExpiryYear = paymentMethod.ExpiryYear,
        CreatedAt = paymentMethod.CreatedAt
    };
}
