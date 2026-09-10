using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Raw card details supplied by a caller (one-off payment or card to vault). These are passed straight
/// to PayPal and never persisted or logged by this application.
/// </summary>
public class CardDto
{
    /// <summary>The card number, e.g. the sandbox test card 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in ISO-8601 YYYY-MM form, e.g. "2027-12".</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalCardDetails ToDomain() => new(
        Number, Expiry, SecurityCode, Name, AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToDomain() => new(
        string.IsNullOrWhiteSpace(Street) ? "N/A" : Street,
        string.IsNullOrWhiteSpace(City) ? "N/A" : City,
        string.IsNullOrWhiteSpace(State) ? "N/A" : State,
        string.IsNullOrWhiteSpace(Country) ? "N/A" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "00000" : ZipCode);
}

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;

    public static RefundDto From(RefundView v) => new()
    {
        Id = v.Id,
        PayPalRefundId = v.PayPalRefundId,
        Amount = v.Amount,
        Status = v.Status
    };
}

public class PaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PayPalOrderId { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? RefundableRemaining { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto? From(PaymentView? v)
    {
        if (v is null) return null;
        return new PaymentDto
        {
            Currency = v.Currency,
            Amount = v.Amount,
            PayPalOrderId = v.PayPalOrderId,
            AuthorizationId = v.AuthorizationId,
            AuthorizationStatus = v.AuthorizationStatus,
            AuthorizationExpiresAt = v.AuthorizationExpiresAt,
            CaptureId = v.CaptureId,
            CaptureStatus = v.CaptureStatus,
            CapturedAmount = v.CapturedAmount,
            PayPalFee = v.PayPalFee,
            NetAmount = v.NetAmount,
            RefundableRemaining = v.RefundableRemaining,
            CardBrand = v.CardBrand,
            CardLast4 = v.CardLast4,
            Refunds = v.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderDto From(OrderView v) => new()
    {
        OrderId = v.OrderId,
        OrderDate = v.OrderDate,
        Status = v.Status.ToString(),
        Total = v.Total,
        Items = v.Items.Select(i => new OrderItemDto
        {
            CatalogItemId = i.CatalogItemId,
            ProductName = i.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = PaymentDto.From(v.Payment)
    };
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static SavedCardDto From(SavedCardView v) => new()
    {
        PaymentMethodId = v.PaymentMethodId,
        Brand = v.Brand,
        Last4 = v.Last4,
        Expiry = v.Expiry,
        CardholderName = v.CardholderName
    };
}
