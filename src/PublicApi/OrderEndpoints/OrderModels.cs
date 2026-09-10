using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ------- Request models -------

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Card details for a one-off payment or to save a card. Passed straight to PayPal; never stored.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // "YYYY-MM"
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress is null
            ? null
            : new CardBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
                BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode, BillingAddress.CountryCode));
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    // Server-set from the JWT; never bound from the request body.
    internal string BuyerId { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }

    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

// ------- Response models -------

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;
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

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderDto From(Order order)
    {
        var dto = new OrderDto
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
            }).ToList()
        };

        if (order.Payment is { } p)
        {
            dto.Payment = new PaymentDto
            {
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                AuthorizationStatus = p.AuthorizationStatus,
                AuthorizationExpiresAt = p.AuthorizationExpiresAt,
                AuthorizedAmount = p.AuthorizedAmount,
                Currency = p.Currency,
                Instrument = p.PaymentInstrumentDescription,
                CaptureId = p.CaptureId,
                CaptureStatus = p.CaptureStatus,
                CapturedAmount = p.CapturedAmount,
                PayPalFee = p.PayPalFee,
                NetAmount = p.NetAmount,
                TotalRefunded = p.TotalRefunded,
                RefundableRemaining = p.RefundableRemaining,
                Refunds = p.Refunds.Select(r => new RefundDto
                {
                    RefundId = r.RefundId,
                    Amount = r.Amount,
                    Currency = r.Currency,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };
        }

        return dto;
    }
}
