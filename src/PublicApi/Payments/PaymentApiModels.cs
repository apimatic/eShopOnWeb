using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Raw card details supplied by the caller for a one-off payment or to save a card.</summary>
public class CardDto
{
    public string CardholderName { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressDto? BillingAddress { get; set; }
}

/// <summary>Billing address for a card.</summary>
public class BillingAddressDto
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>A single line of an order (safe to return).</summary>
public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order with its payment state (safe to return).</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>A saved card described safely — brand, last four digits and expiry, never full details.</summary>
public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

/// <summary>Maps between the API DTOs and the domain payment models.</summary>
public static class PaymentApiMappings
{
    public static CardDetails ToCardDetails(CardDto dto)
    {
        var billing = dto.BillingAddress ?? new BillingAddressDto();
        return new CardDetails(
            dto.CardholderName,
            dto.Number,
            dto.ExpiryMonth,
            dto.ExpiryYear,
            dto.SecurityCode,
            new BillingAddress(
                billing.AddressLine1,
                billing.City,
                billing.PostalCode,
                billing.CountryCode,
                billing.State,
                billing.AddressLine2));
    }

    public static OrderSummaryDto ToSummary(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        PayPalCaptureId = order.PayPalCaptureId,
        PayPalRefundId = order.PayPalRefundId,
        Items = order.OrderItems.Select(item => new OrderItemDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Units = item.Units
        }).ToList()
    };

    public static SavedCardDto ToSavedCard(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Alias = paymentMethod.Alias,
        CardBrand = paymentMethod.CardBrand,
        Last4 = paymentMethod.Last4,
        Expiry = paymentMethod.Expiry
    };
}
