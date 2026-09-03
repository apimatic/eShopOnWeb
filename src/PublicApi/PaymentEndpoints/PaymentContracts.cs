using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- requests ----

/// <summary>Card details for a one-off card payment or for vaulting. Full number is never stored/logged.</summary>
public class CardInput
{
    public string Number { get; set; } = null!;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = null!;
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        $"{ExpiryYear:0000}-{ExpiryMonth:00}",
        SecurityCode,
        Name,
        BillingAddressLine1,
        BillingAddressLine2,
        BillingCity,
        BillingState,
        BillingPostalCode,
        BillingCountryCode);
}

public class CreateOrderRequest
{
    public List<OrderLineInput> Items { get; set; } = new();
    public ShippingAddressInput? ShipTo { get; set; }
}

public record OrderLineInput(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>Pay by one-off card (<see cref="Card"/>) or by a saved card (<see cref="SavedCardId"/>).</summary>
public class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? SavedCardId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for the full remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class SavePaymentMethodRequest
{
    public CardInput Card { get; set; } = null!;
    public string? Label { get; set; }
}

// ---- responses ----

public record RefundView(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

/// <summary>The payment/fulfilment view of an order. <c>OrderId</c> is a top-level field.</summary>
public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = null!;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
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
    public IReadOnlyList<RefundView> Refunds { get; set; } = Array.Empty<RefundView>();

    public static OrderPaymentResponse From(Order order, string fallbackCurrency) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus.ToString(),
        Total = order.Total(),
        Currency = order.Currency ?? fallbackCurrency,
        PayPalOrderId = order.PayPalOrderId,
        AuthorizationId = order.AuthorizationId,
        AuthorizationStatus = order.AuthorizationStatus,
        AuthorizationExpiresAt = order.AuthorizationExpiresAt,
        CaptureId = order.CaptureId,
        CaptureStatus = order.CaptureStatus,
        CapturedAmount = order.CapturedAmount,
        PayPalFee = order.PayPalFee,
        NetAmount = order.NetAmount,
        TotalRefunded = order.TotalRefunded(),
        RefundableRemaining = order.RefundableRemaining(),
        Refunds = order.Refunds
            .Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList()
    };
}

/// <summary><c>RefundId</c> is a top-level field.</summary>
public class RefundResponse
{
    public string RefundId { get; set; } = null!;
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = null!;
    public decimal RefundedAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public string? Currency { get; set; }
}

/// <summary><c>PaymentMethodId</c> is a top-level field.</summary>
public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodResponse From(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.LastFourDigits,
        Expiry = card.Expiry,
        Label = card.Label,
        CreatedAt = card.CreatedAt
    };
}
