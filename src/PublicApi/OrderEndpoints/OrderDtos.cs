using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A shopper-facing view of an order and its payment state.</summary>
public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public static OrderDto From(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.Payment?.Currency,
        PaymentStatus = order.PaymentStatus.ToString(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = order.Payment is null ? null : new PaymentDto
        {
            PayPalOrderId = order.Payment.PayPalOrderId,
            Reference = order.Payment.Reference,
            AuthorizationId = order.Payment.AuthorizationId,
            AuthorizationStatus = order.Payment.AuthorizationStatus,
            CaptureId = order.Payment.CaptureId,
            CaptureStatus = order.Payment.CaptureStatus,
            CapturedAmount = order.Payment.CapturedAmount,
            PayPalFee = order.Payment.PayPalFee,
            NetAmount = order.Payment.NetAmount
        },
        Refunds = order.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList(),
        TotalRefunded = order.TotalRefunded(),
        RefundableRemaining = order.RefundableRemaining()
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A single catalog line on a new order.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>An optional shipping address for a new order.</summary>
public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address? ToAddress() =>
        string.IsNullOrWhiteSpace(Street) && string.IsNullOrWhiteSpace(City) && string.IsNullOrWhiteSpace(ZipCode)
            ? null
            : new Address(Street ?? "N/A", City ?? "N/A", State ?? "N/A", Country ?? "N/A", ZipCode ?? "00000");
}

/// <summary>Raw card details for a one-off payment or to be vaulted. Never stored or logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry. Accepts "YYYY-MM", "MM/YY" or "MM/YYYY".</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number?.Replace(" ", "").Trim() ?? string.Empty,
        NormalizeExpiry(Expiry),
        SecurityCode?.Trim() ?? string.Empty,
        Name,
        BillingAddress?.ToModel());

    /// <summary>Normalizes common expiry formats to PayPal's "YYYY-MM".</summary>
    public static string NormalizeExpiry(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return expiry;
        expiry = expiry.Trim();

        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}$")) return expiry;               // YYYY-MM

        var slash = Regex.Match(expiry, @"^(\d{1,2})\s*/\s*(\d{2}|\d{4})$");       // MM/YY or MM/YYYY
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value).ToString("D2");
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2 ? $"20{yearPart}" : yearPart;
            return $"{year}-{month}";
        }
        return expiry; // pass through; PayPal will validate
    }
}

public class CardBillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToModel() => new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
