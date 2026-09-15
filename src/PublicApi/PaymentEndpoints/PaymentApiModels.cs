using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------------------------------------------------------------- requests

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

/// <summary>An operator action against an order identified purely by its route id (no request body).</summary>
public class OrderActionRequest
{
    public int OrderId { get; init; }
    public OrderActionRequest(int orderId) => OrderId = orderId;
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>Two-letter ISO-3166 country code (defaults to "US" when omitted).</summary>
    public string? CountryCode { get; set; }
}

/// <summary>Raw card details for a one-off payment or to save a card. Never persisted or logged.</summary>
public class CardDto
{
    public string? CardNumber { get; set; }
    /// <summary>Card expiry in YYYY-MM (PayPal date_year_month format).</summary>
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Set from the route, not the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Provide this or <see cref="PaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }
    /// <summary>A saved card id to pay with instead of raw card details.</summary>
    public int? PaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Set from the route, not the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

// ---------------------------------------------------------------- responses

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class AuthorizationDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureDto
{
    public string? CaptureId { get; set; }
    public string? Status { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>A safe, full view of an order and its payment/fulfilment state. Carries <c>orderId</c> at the top level.</summary>
public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class SavedCardResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// ---------------------------------------------------------------- mapping helpers

public static class PaymentApiMapper
{
    public static OrderPaymentResponse ToResponse(Order order)
    {
        var response = new OrderPaymentResponse
        {
            OrderId = order.Id,
            Status = order.PaymentStatus.ToString(),
            Currency = order.Currency,
            Total = order.Total(),
            OrderDate = order.OrderDate,
            TotalRefunded = order.TotalRefunded(),
            RefundableRemaining = order.RefundableRemaining(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status
            }).ToList()
        };

        if (order.PayPalAuthorizationId is not null)
        {
            response.Authorization = new AuthorizationDto
            {
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                Status = order.AuthorizationStatus,
                ExpiresAt = order.AuthorizationExpiresAt
            };
        }

        if (order.PayPalCaptureId is not null)
        {
            response.Capture = new CaptureDto
            {
                CaptureId = order.PayPalCaptureId,
                Status = order.CaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PayPalFee = order.PayPalFee,
                NetAmount = order.NetAmount
            };
        }

        return response;
    }

    public static SavedCardResponse ToResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardHolderName,
        CreatedAt = method.CreatedAt
    };

    public static CardDetails ToCardDetails(CardDto card)
    {
        var billing = card.BillingAddress;
        return new CardDetails(
            card.CardNumber ?? string.Empty,
            card.Expiry ?? string.Empty,
            card.SecurityCode ?? string.Empty,
            card.CardholderName ?? string.Empty,
            new BillingAddressDetails(
                billing?.AddressLine1,
                billing?.AddressLine2,
                billing?.City,
                billing?.State,
                billing?.PostalCode,
                string.IsNullOrWhiteSpace(billing?.CountryCode) ? "US" : billing!.CountryCode!.Trim().ToUpperInvariant()));
    }
}

public static class PaymentClaimsExtensions
{
    /// <summary>
    /// The caller's buyer id (the JWT subject / user name), which matches <c>Order.BuyerId</c> in the
    /// existing model. Every shopper-scoped endpoint acts only on data owned by this id.
    /// </summary>
    public static string GetBuyerId(this ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name
                   ?? user?.FindFirst(ClaimTypes.Name)?.Value
                   ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The bearer token does not identify a shopper.");
        }
        return name;
    }
}
