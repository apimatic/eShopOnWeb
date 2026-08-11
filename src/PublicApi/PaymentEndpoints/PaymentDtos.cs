using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details a caller supplies for a one-off payment or to save. Never persisted or logged in full.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}

/// <summary>A single refund, safe for display.</summary>
public record RefundDto(string RefundId, decimal Amount, string Status, string IdempotencyKey, DateTimeOffset CreatedAt);

/// <summary>The payment state for an order, carrying the PayPal-owned ids/status a later request can act on.</summary>
public record PaymentStateDto(
    int OrderId,
    string Status,
    string CurrencyCode,
    decimal Amount,
    string? PaymentInstrument,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    decimal? CapturedGross,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundDto> Refunds);

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string CurrencyCode,
    string PaymentStatus,
    IReadOnlyList<OrderItemDto> Items,
    PaymentStateDto? Payment);

public record SavedCardDto(
    int PaymentMethodId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName,
    string? Label,
    string DisplayName,
    DateTimeOffset CreatedAt);

/// <summary>Maps domain entities to safe API DTOs.</summary>
public static class PaymentMapping
{
    public static CardDetails ToCardDetails(this CardDto card) => new(
        card.Number,
        card.ExpiryMonth,
        card.ExpiryYear,
        card.SecurityCode,
        card.CardholderName,
        card.BillingAddressLine1,
        card.BillingAddressLine2,
        card.BillingCity,
        card.BillingState,
        card.BillingPostalCode,
        card.BillingCountryCode);

    public static PaymentStateDto ToDto(this OrderPayment payment) => new(
        payment.OrderId,
        payment.Status.ToString(),
        payment.CurrencyCode,
        payment.Amount,
        payment.PaymentInstrumentDescription,
        payment.PayPalOrderId,
        payment.AuthorizationId,
        payment.AuthorizationStatus,
        payment.AuthorizationExpiresAt,
        payment.CaptureId,
        payment.CapturedGross,
        payment.PayPalFee,
        payment.NetAmount,
        payment.TotalRefunded(),
        payment.RefundableRemaining(),
        payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto(r.PayPalRefundId, r.Amount, r.Status, r.IdempotencyKey, r.CreatedAt))
            .ToList());

    public static SavedCardDto ToDto(this SavedCard card) => new(
        card.Id,
        card.Brand,
        card.Last4,
        card.Expiry,
        card.CardholderName,
        card.Label,
        card.DisplayName(),
        card.CreatedAt);

    public static MyOrderDto ToDto(this Order order, OrderPayment? payment) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        payment?.CurrencyCode ?? string.Empty,
        (payment?.Status ?? OrderPaymentStatus.AwaitingPayment).ToString(),
        order.OrderItems
            .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList(),
        payment?.ToDto());
}
