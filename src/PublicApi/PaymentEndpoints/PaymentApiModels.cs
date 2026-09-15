using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by a shopper for a one-off payment or to be saved. Never persisted here.</summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in PayPal's format, e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>State / province (PayPal admin_area_1).</summary>
    public string? State { get; set; }
    /// <summary>City (PayPal admin_area_2).</summary>
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter ISO country code (PayPal country_code).</summary>
    public string? CountryCode { get; set; }

    public PayPalCardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        AddressLine1,
        AddressLine2,
        State,
        City,
        PostalCode,
        CountryCode);
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
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Instrument { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Maps domain entities to the API response shapes. Never exposes card numbers or PayPal secrets.</summary>
public static class PaymentApiMapper
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
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
    }

    public static PaymentDto ToDto(OrderPayment payment)
    {
        return new PaymentDto
        {
            Currency = payment.Currency,
            Amount = payment.Amount,
            Instrument = payment.InstrumentDescription,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedGross = payment.CapturedGross,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            RefundableRemaining = payment.RefundableRemaining,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    public static PaymentMethodDto ToDto(SavedPaymentMethod method)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = method.Id,
            DisplayName = method.DisplayName,
            Brand = method.CardBrand,
            Last4 = method.LastFourDigits,
            Expiry = method.CardExpiry,
            CreatedAt = method.CreatedAt
        };
    }
}
