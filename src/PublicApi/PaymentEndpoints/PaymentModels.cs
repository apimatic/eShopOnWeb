using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------- Request bodies ----------

public class PlaceOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Pay with one of the shopper's saved cards. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Pay with a one-off card. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }
}

public class RefundRequest
{
    /// <summary>Amount to refund; omit for a full refund of what remains.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---------- Response bodies ----------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public IReadOnlyList<OrderLineDto> Items { get; set; } = Array.Empty<OrderLineDto>();
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public IReadOnlyList<RefundDto> Refunds { get; set; } = Array.Empty<RefundDto>();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class CancelResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public PaymentResponse? Payment { get; set; }
    public IReadOnlyList<OrderLineDto> Items { get; set; } = Array.Empty<OrderLineDto>();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInPayPalCount { get; set; }
    public int OnlyInEShopCount { get; set; }
    public IReadOnlyList<ReconciliationLineDto> Matched { get; set; } = Array.Empty<ReconciliationLineDto>();
    public IReadOnlyList<ReconciliationLineDto> OnlyInPayPal { get; set; } = Array.Empty<ReconciliationLineDto>();
    public IReadOnlyList<ReconciliationLineDto> OnlyInEShop { get; set; } = Array.Empty<ReconciliationLineDto>();
}

public class ReconciliationLineDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? Date { get; set; }
}

// ---------- Mapping helpers ----------

public static class PaymentMapping
{
    public static IReadOnlyList<OrderLineDto> ToLineDtos(Order order) =>
        order.OrderItems.Select(i => new OrderLineDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();

    public static OrderStatus DeriveOrderStatus(Payment payment) => payment.Status switch
    {
        PaymentStatus.Authorized => OrderStatus.PaymentAuthorized,
        PaymentStatus.Captured => OrderStatus.Fulfilled,
        PaymentStatus.Voided => OrderStatus.Cancelled,
        PaymentStatus.Refunded => OrderStatus.Refunded,
        PaymentStatus.PartiallyRefunded => OrderStatus.PartiallyRefunded,
        _ => OrderStatus.AwaitingPayment
    };

    public static PaymentResponse ToPaymentResponse(Payment payment) =>
        ToPaymentResponse(payment, DeriveOrderStatus(payment));

    public static PaymentResponse ToPaymentResponse(Payment payment, OrderStatus orderStatus) => new()
    {
        OrderId = payment.OrderId,
        OrderStatus = orderStatus.ToString(),
        PaymentStatus = payment.Status.ToString(),
        Currency = payment.Currency,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        SavedPaymentMethodId = payment.SavedPaymentMethodId,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    public static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.CardBrand,
        Last4 = card.Last4,
        ExpiryMonth = card.ExpiryMonth,
        ExpiryYear = card.ExpiryYear,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };

    public static ReconciliationLineDto ToLineDto(ReconciliationLine line) => new()
    {
        TransactionId = line.TransactionId,
        Kind = line.Kind,
        Amount = line.Amount,
        Currency = line.Currency,
        Status = line.Status,
        OrderId = line.OrderId,
        Date = line.Date
    };

    public static CardDetails ToCardDetails(CardDto card) => new(
        card.Number,
        card.ExpiryMonth,
        card.ExpiryYear,
        card.SecurityCode,
        card.CardholderName,
        card.BillingAddress is null
            ? null
            : new CardBillingAddress(
                card.BillingAddress.Line1,
                card.BillingAddress.Line2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
}
