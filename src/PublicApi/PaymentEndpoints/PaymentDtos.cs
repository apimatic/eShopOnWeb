using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details a shopper supplies for a one-off payment or to save. Never persisted or logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;       // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty; // CVV/CVC
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingLine1 = BillingLine1,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingCountryCode = BillingCountryCode,
        BillingPostalCode = BillingPostalCode
    };
}

public class AddressDto
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
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    /// <summary>Raw card for a one-off payment. Provide this OR <see cref="SavedCardId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedCardId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: a repeat under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}

/// <summary>Safe representation of a saved card — never full card details.</summary>
public record SavedCardDto(int PaymentMethodId, string? Brand, string? LastDigits, string? Expiry, string? CardholderName, DateTimeOffset CreatedAt)
{
    public static SavedCardDto From(SavedCard c) =>
        new(c.Id, c.Brand, c.LastDigits, c.Expiry, c.CardholderName, c.CreatedAt);
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardDto? Card { get; set; }
}

public record RefundDto(string? RefundId, decimal Amount, string? Status, DateTimeOffset CreatedAt);

/// <summary>The payment state PayPal owns for an order (ids + current status of hold, capture, refunds).</summary>
public record PaymentStateDto(
    int OrderId,
    string Status,
    string CurrencyCode,
    decimal Amount,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundDto> Refunds)
{
    public static PaymentStateDto From(OrderPayment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.CurrencyCode,
        p.Amount,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.TotalRefunded,
        p.RefundableRemaining,
        p.Refunds.Select(r => new RefundDto(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt)).ToList());
}

public class RefundOrderResponse
{
    public string? RefundId { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public PaymentStateDto? Payment { get; set; }
}

public record OrderSummaryDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    PaymentStateDto? Payment);

public record ReconciliationLineDto(
    string? TransactionId,
    string? OrderReference,
    int? MatchedOrderId,
    decimal? PayPalAmount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? PayPalStatus,
    DateTimeOffset? Date,
    decimal? EShopAmount,
    string? EShopPaymentStatus,
    string MatchState);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopOrderCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

public static class CallerExtensions
{
    /// <summary>The shopper's identity from the JWT (the Name claim), matching Order.BuyerId.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}
