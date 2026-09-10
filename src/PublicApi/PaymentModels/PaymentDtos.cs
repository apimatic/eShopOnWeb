using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

// ---- Shared input shapes -------------------------------------------------------------------

/// <summary>A card's billing address. PayPal requires at least a country code.</summary>
public record BillingAddressDto(string? Line1, string? Line2, string? City, string? State, string? PostalCode, string? CountryCode);

/// <summary>Raw card details for a one-off payment or for saving. Never stored or logged by this app.</summary>
public record CardDto(string Number, string Expiry, string SecurityCode, string? Name, BillingAddressDto? BillingAddress);

/// <summary>An optional shipping address for an order.</summary>
public record OrderAddressDto(string? Street, string? City, string? State, string? Country, string? ZipCode);

// ---- Order line item -----------------------------------------------------------------------

public record OrderItemLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

// ---- Payment / order views -----------------------------------------------------------------

/// <summary>The PayPal-owned payment state for an order, in safe terms.</summary>
public record PaymentDto(
    string Status,
    string Currency,
    decimal AuthorizedAmount,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyCollection<RefundDto> Refunds);

public record RefundDto(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

/// <summary>A view of an order together with its current payment state.</summary>
public record OrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    string Currency,
    decimal Total,
    IReadOnlyCollection<OrderItemLineDto> Items,
    PaymentDto? Payment);

/// <summary>A saved card described safely enough to recognise, with no card details.</summary>
public record PaymentMethodDto(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? CardHolderName, string? Alias, DateTimeOffset CreatedAt);

/// <summary>Maps domain entities to the API view models.</summary>
public static class PaymentMapping
{
    public static OrderDto ToOrderDto(Order order, string currency)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemLineDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new OrderDto(
            OrderId: order.Id,
            Status: order.Status.ToString(),
            OrderDate: order.OrderDate,
            Currency: currency,
            Total: order.Total(),
            Items: items,
            Payment: order.Payment is null ? null : ToPaymentDto(order.Payment));
    }

    public static PaymentDto ToPaymentDto(Payment payment)
    {
        var refunds = payment.Refunds
            .Select(r => new RefundDto(r.RefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList();

        return new PaymentDto(
            Status: payment.Status.ToString(),
            Currency: payment.Currency,
            AuthorizedAmount: payment.AuthorizedAmount,
            PayPalOrderId: payment.PayPalOrderId,
            AuthorizationId: payment.AuthorizationId,
            AuthorizationStatus: payment.AuthorizationStatus,
            AuthorizationExpiresAt: payment.AuthorizationExpiresAt,
            CaptureId: payment.CaptureId,
            CaptureStatus: payment.CaptureStatus,
            CapturedAmount: payment.CapturedAmount,
            PayPalFee: payment.PayPalFee,
            NetAmount: payment.NetAmount,
            TotalRefunded: payment.TotalRefunded,
            RefundableRemaining: payment.RefundableRemaining,
            Refunds: refunds);
    }

    public static PaymentMethodDto ToPaymentMethodDto(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CardHolderName, method.Alias, method.CreatedAt);

    public static CardDetails ToCardDetails(CardDto card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            var b = card.BillingAddress;
            billing = new CardBillingAddress(b.Line1, b.Line2, b.City, b.State, b.PostalCode, b.CountryCode ?? "US");
        }
        return new CardDetails(card.Number, card.Expiry, card.SecurityCode, card.Name, billing);
    }
}
