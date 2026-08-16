using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request payloads ----

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";
}

public class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Raw card details. Never persisted or logged by the app.</summary>
public class CardRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string Cvc { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

// ---- Response DTOs ----

public class RefundDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public int OrderId { get; set; }
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
    public decimal? NetProceeds { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }

    public int? SavedPaymentMethodId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }
}

public class SavedCardDto
{
    public int Id { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Mapping helpers between domain objects and API DTOs.</summary>
public static class PaymentMapper
{
    public static PaymentCard ToPaymentCard(CardRequest card)
    {
        PaymentBillingAddress? billing = null;
        if (card.BillingAddress is { } b)
        {
            billing = new PaymentBillingAddress(b.Line1, b.Line2, b.City, b.State, b.PostalCode, b.CountryCode);
        }

        return new PaymentCard(card.CardNumber, card.ExpiryMonth, card.ExpiryYear, card.Cvc,
            card.CardholderName, billing);
    }

    public static PaymentStateDto ToStateDto(OrderPayment payment)
    {
        return new PaymentStateDto
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.CurrencyCode,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedGrossAmount,
            PayPalFee = payment.PayPalFee,
            NetProceeds = payment.NetAmount,
            CapturedAt = payment.CapturedAt,
            SavedPaymentMethodId = payment.SavedPaymentMethodId,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
            Refunds = payment.Refunds
                .OrderBy(r => r.Id)
                .Select(r => new RefundDto
                {
                    Id = r.Id,
                    Amount = r.Amount,
                    Status = r.Status,
                    PayPalRefundId = r.PayPalRefundId,
                    CreatedAt = r.CreatedAt
                }).ToList()
        };
    }

    public static OrderSummaryDto ToSummaryDto(Order order, OrderPayment? payment)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                Units = i.Units,
                UnitPrice = i.UnitPrice
            }).ToList(),
            Payment = payment is null ? null : ToStateDto(payment)
        };
    }

    public static SavedCardDto ToSavedCardDto(SavedPaymentMethod method)
    {
        return new SavedCardDto
        {
            Id = method.Id,
            Brand = method.Brand,
            LastFourDigits = method.LastFourDigits,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName,
            CreatedAt = method.CreatedAt
        };
    }
}
