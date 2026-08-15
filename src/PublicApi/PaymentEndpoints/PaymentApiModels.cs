using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details a shopper submits to pay once or to save. Never persisted or logged.</summary>
public class CardDto
{
    public string CardholderName { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in "YYYY-MM" form (e.g. "2027-01").</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new(
        CardholderName, Number, Expiry, SecurityCode,
        BillingCountryCode, BillingAddressLine, BillingCity, BillingState, BillingPostalCode);
}

/// <summary>A single refund, echoing PayPal's record. Safe to return to the shopper.</summary>
public class RefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(PaymentRefund r) => new()
    {
        Id = r.Id,
        PayPalRefundId = r.PayPalRefundId,
        Amount = r.Amount,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}

/// <summary>The full payment state of an order, including everything PayPal owns.</summary>
public class PaymentDto
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
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public int? PaymentMethodId { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto From(Payment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        Currency = p.CurrencyCode,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded,
        RefundableRemaining = p.RefundableRemaining,
        PaymentMethodId = p.PaymentMethodId,
        Refunds = p.Refunds.Select(RefundDto.From).OrderBy(r => r.CreatedAt).ToList()
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>An order paired with its payment state, for the "my orders" view.</summary>
public class OrderWithPaymentDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderWithPaymentDto From(OrderPaymentSnapshot snapshot)
    {
        var order = snapshot.Order;
        return new OrderWithPaymentDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = snapshot.Payment is null ? null : PaymentDto.From(snapshot.Payment)
        };
    }
}

/// <summary>A saved card, described safely (no full number).</summary>
public class PaymentMethodDto
{
    public int Id { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(PaymentMethod pm) => new()
    {
        Id = pm.Id,
        Brand = pm.Brand,
        LastDigits = pm.LastDigits,
        Expiry = pm.Expiry,
        CreatedAt = pm.CreatedAt
    };
}

internal static class CallerExtensions
{
    /// <summary>The signed-in shopper's identity (username) — this is the order/basket BuyerId.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
