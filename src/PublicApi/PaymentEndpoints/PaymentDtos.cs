using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Shared input DTOs ----

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    /// <summary>City.</summary>
    public string? AdminArea2 { get; set; }
    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>2-letter ISO-3166-1 country code (required by PayPal).</summary>
    public string CountryCode { get; set; } = string.Empty;
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// ---- Shared output DTOs ----

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
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
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

/// <summary>Maps domain entities to the API response shapes.</summary>
public static class PaymentMapping
{
    public static OrderPaymentDto ToDto(OrderPaymentView view)
    {
        var order = view.Order;
        return new OrderPaymentDto
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = view.Payment is null ? null : ToDto(view.Payment)
        };
    }

    public static PaymentDto ToDto(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            }).ToList()
    };

    public static GatewayCardDetails ToGatewayCard(CardDto card) => new(
        Number: card.Number,
        Expiry: card.Expiry,
        SecurityCode: card.SecurityCode,
        Name: card.Name,
        BillingAddress: card.BillingAddress is null ? null : new GatewayBillingAddress(
            AddressLine1: card.BillingAddress.AddressLine1,
            AddressLine2: card.BillingAddress.AddressLine2,
            AdminArea2: card.BillingAddress.AdminArea2,
            AdminArea1: card.BillingAddress.AdminArea1,
            PostalCode: card.BillingAddress.PostalCode,
            CountryCode: card.BillingAddress.CountryCode));
}
