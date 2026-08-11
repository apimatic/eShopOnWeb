using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by a caller for a one-off payment or to save a card.</summary>
public class CardRequest
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

    public PayPalCardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
            throw new PaymentValidationException("Card number is required.");
        if (string.IsNullOrWhiteSpace(SecurityCode))
            throw new PaymentValidationException("Card security code is required.");
        if (ExpiryMonth is < 1 or > 12)
            throw new PaymentValidationException("Card expiry month must be between 1 and 12.");
        if (ExpiryYear is < 2000 or > 2100)
            throw new PaymentValidationException("Card expiry year is invalid.");

        return new PayPalCardDetails(
            Number: Number.Trim(),
            Expiry: $"{ExpiryYear:D4}-{ExpiryMonth:D2}",
            SecurityCode: SecurityCode.Trim(),
            Name: CardholderName,
            BillingAddressLine1: BillingAddressLine1,
            BillingAddressLine2: BillingAddressLine2,
            AdminArea2: BillingCity,
            AdminArea1: BillingState,
            PostalCode: BillingPostalCode,
            CountryCode: string.IsNullOrWhiteSpace(BillingCountryCode) ? "US" : BillingCountryCode.Trim());
    }
}

/// <summary>A shipping address for a placed order.</summary>
public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

// --- Response DTOs ---

public record PaymentCardDto(string? Brand, string? Last4);

public record RefundDto(string RefundId, decimal Amount, string Status, DateTimeOffset RefundedAt);

public record PaymentDto(
    string Status,
    string Currency,
    decimal AuthorizedAmount,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CapturedAt,
    PaymentCardDto? Card,
    IReadOnlyList<RefundDto> Refunds,
    decimal TotalRefunded,
    decimal RefundableRemaining);

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    string? Currency,
    PaymentDto? Payment,
    IReadOnlyList<OrderItemDto> Items);

/// <summary>Maps domain entities to safe response DTOs and resolves the caller's identity.</summary>
public static class PaymentMapper
{
    /// <summary>The buyer id is the caller's token identity — the sole source of who the caller is.</summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.Name)
                 ?? user.FindFirstValue("unique_name")
                 ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentValidationException("The access token does not identify a user.");
        }
        return id;
    }

    public static int GetRouteInt(HttpContext ctx, string key)
    {
        if (ctx.Request.RouteValues.TryGetValue(key, out var value) &&
            int.TryParse(value?.ToString(), out var parsed))
        {
            return parsed;
        }
        throw new PaymentValidationException($"Route value '{key}' is missing or not a valid integer.");
    }

    public static PaymentDto? ToPaymentDto(Payment? payment)
    {
        if (payment is null) return null;

        var card = (payment.CardBrand is not null || payment.CardLast4 is not null)
            ? new PaymentCardDto(payment.CardBrand, payment.CardLast4)
            : null;

        var refunds = payment.Refunds
            .Select(r => new RefundDto(r.PayPalRefundId, r.Amount, r.Status, r.RefundedAt))
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
            CapturedAt: payment.CapturedAt,
            Card: card,
            Refunds: refunds,
            TotalRefunded: payment.TotalRefunded,
            RefundableRemaining: payment.RefundableRemaining);
    }

    public static OrderDto ToOrderDto(Order order)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new OrderDto(
            OrderId: order.Id,
            Status: order.Status.ToString(),
            OrderDate: order.OrderDate,
            Total: order.Total(),
            Currency: order.Payment?.Currency,
            Payment: ToPaymentDto(order.Payment),
            Items: items);
    }
}
