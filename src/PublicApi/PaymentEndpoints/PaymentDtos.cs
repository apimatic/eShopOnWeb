using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Resolves the caller's identity (the buyer id) from the validated JWT.</summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException("The caller's identity could not be determined from the token.");
        }
        return name;
    }
}

// ---- request bodies ----

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderRequest
{
    /// <summary>One-off card to pay with. Supply this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of a one-off card.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

// ---- response bodies ----

public class PaymentStateDto
{
    public string Provider { get; set; } = Payment.PayPalProvider;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

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
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundStateDto> Refunds { get; set; } = new();

    public static PaymentStateDto From(Payment payment) => new()
    {
        Provider = payment.Provider,
        Status = payment.Status.ToString(),
        Currency = payment.CurrencyCode,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedGrossAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableAmount(),
        SavedPaymentMethodId = payment.SavedCardId,
        Refunds = payment.Refunds
            .Select(r => new RefundStateDto { RefundId = r.PayPalRefundId, Amount = r.Amount, Status = r.Status, CreatedAt = r.CreatedAt })
            .ToList()
    };
}

public class RefundStateDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentStateDto? Payment { get; set; }

    public static MyOrderDto From(Order order, Payment? payment) => new()
    {
        OrderId = order.Id,
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
        Payment = payment is null ? null : PaymentStateDto.From(payment)
    };
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedCardDto From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}
