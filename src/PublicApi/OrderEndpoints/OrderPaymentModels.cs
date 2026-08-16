using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ---------- Requests ----------

public class OrderLineRequest
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

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

/// <summary>Card details for a one-off payment or to vault. Never stored by this app, never logged.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty; // MM
    public string ExpiryYear { get; set; } = string.Empty;  // YYYY
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Pay with either raw card details or one of the shopper's saved cards (not both).</summary>
public class PayOrderRequest
{
    public int OrderId { get; set; }
    public CardRequestDto? Card { get; set; }
    public int? SavedCardId { get; set; }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: a repeat under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

// ---------- Responses ----------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class PaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? CardDescription { get; set; }
    public int? SavedCardId { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto From(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        CaptureId = p.CaptureId,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        RefundedAmount = p.RefundedAmount(),
        CardDescription = p.CardDescription,
        SavedCardId = p.SavedCardId,
        AuthorizedAt = p.AuthorizedAt,
        CapturedAt = p.CapturedAt,
        Refunds = MapRefunds(p),
    };

    private static List<RefundDto> MapRefunds(OrderPayment p)
    {
        var list = new List<RefundDto>();
        foreach (var r in p.Refunds)
        {
            list.Add(new RefundDto
            {
                Id = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
            });
        }
        return list;
    }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();

    public static OrderSummaryDto From(Order order, OrderPayment payment)
    {
        var dto = new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Payment = PaymentDto.From(payment),
        };
        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderLineDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
            });
        }
        return dto;
    }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>Maps the API card DTO onto the gateway's transient card model.</summary>
public static class CardRequestMapping
{
    public static CardPaymentDetails ToCardPaymentDetails(this CardRequestDto dto)
    {
        CardBillingAddress? billing = null;
        if (dto.BillingAddress is not null)
        {
            var b = dto.BillingAddress;
            billing = new CardBillingAddress(b.AddressLine1, b.AddressLine2, b.State, b.City, b.PostalCode, b.CountryCode);
        }
        return new CardPaymentDetails(dto.Number, dto.ExpiryMonth, dto.ExpiryYear, dto.SecurityCode,
            dto.CardholderName, billing);
    }
}
