using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied by the caller. Never persisted or logged.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
            throw new PaymentException("Card number is required.");
        if (ExpiryMonth is < 1 or > 12)
            throw new PaymentException("Card expiry month must be between 1 and 12.");
        if (ExpiryYear < 2000 || ExpiryYear > 2100)
            throw new PaymentException("Card expiry year must be a four-digit year.");
        if (string.IsNullOrWhiteSpace(SecurityCode))
            throw new PaymentException("Card security code is required.");

        return new CardDetails
        {
            Number = Number.Replace(" ", string.Empty).Trim(),
            ExpiryYearMonth = $"{ExpiryYear:D4}-{ExpiryMonth:D2}",
            SecurityCode = SecurityCode.Trim(),
            Name = Name,
            AddressLine1 = AddressLine1,
            AddressLine2 = AddressLine2,
            City = City,
            State = State,
            PostalCode = PostalCode,
            CountryCode = CountryCode
        };
    }
}

/// <summary>Safe, display-only view of a saved card.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Alias = pm.Alias,
        CardBrand = pm.CardBrand,
        Last4 = pm.Last4,
        Expiry = pm.Expiry,
        CreatedAt = pm.CreatedAt
    };
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
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The payment state PayPal owns for an order.</summary>
public class OrderPaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto? From(OrderPayment? p)
    {
        if (p is null) return null;
        return new OrderPaymentDto
        {
            Currency = p.Currency,
            Amount = p.Amount,
            PayPalOrderId = p.PayPalOrderId,
            AuthorizationId = p.AuthorizationId,
            AuthorizationStatus = p.AuthorizationStatus,
            AuthorizationExpiresAt = p.AuthorizationExpiresAt,
            CaptureId = p.CaptureId,
            CaptureStatus = p.CaptureStatus,
            CapturedGross = p.CapturedGross,
            PayPalFee = p.PayPalFee,
            NetProceeds = p.NetAmount,
            TotalRefunded = p.TotalRefunded(),
            RemainingRefundable = p.RemainingRefundable(),
            Refunds = p.Refunds
                .Select(r => new RefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
        };
    }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto? Payment { get; set; }

    public static OrderDto From(Order order) => new()
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
        Payment = OrderPaymentDto.From(order.Payment)
    };
}
