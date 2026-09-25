using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- request card model shared by pay + save-card ---------------------------------------

/// <summary>Card details posted by a shopper. Never stored or logged by this app.</summary>
public class CardRequestModel
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;       // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressModel? BillingAddress { get; set; }

    public PayPalCardDetails ToDomain() => new(
        Number, Expiry, SecurityCode, CardholderName, BillingAddress?.ToDomain());
}

public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalBillingAddress ToDomain() => new(AddressLine1, City, State, PostalCode, CountryCode);
}

// ---- response DTOs ----------------------------------------------------------------------

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderPaymentDto(
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedGross,
    decimal? PaypalFee,
    decimal? NetAmount,
    int? SavedPaymentMethodId);

public record OrderRefundDto(string PayPalRefundId, decimal Amount, string Status, DateTimeOffset CreatedAt, string IdempotencyKey);

public record OrderDto(
    int OrderId,
    string Status,
    string? Currency,
    decimal Total,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderItemDto> Items,
    OrderPaymentDto? Payment,
    IReadOnlyList<OrderRefundDto> Refunds,
    decimal TotalRefunded,
    decimal RefundableRemaining);

public record SavedCardDto(int PaymentMethodId, string? Brand, string? LastFourDigits, string? Expiry, string? CardholderName, DateTimeOffset CreatedAt);

/// <summary>Maps domain entities to API DTOs.</summary>
public static class PaymentMappings
{
    public static OrderDto ToDto(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.Payment?.Currency,
        order.Total(),
        order.OrderDate,
        order.OrderItems.Select(i => new OrderItemDto(
            i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        order.Payment is null ? null : new OrderPaymentDto(
            order.Payment.PayPalOrderId,
            order.Payment.AuthorizationId,
            order.Payment.AuthorizationStatus,
            order.Payment.AuthorizationExpiresAt,
            order.Payment.CaptureId,
            order.Payment.CaptureStatus,
            order.Payment.CapturedGross,
            order.Payment.PaypalFee,
            order.Payment.NetAmount,
            order.Payment.SavedPaymentMethodId),
        order.Refunds.Select(r => new OrderRefundDto(
            r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt, r.IdempotencyKey)).ToList(),
        order.TotalRefunded(),
        order.RefundableRemaining());

    public static SavedCardDto ToDto(SavedPaymentMethod card) => new(
        card.Id, card.Brand, card.LastFourDigits, card.Expiry, card.CardholderName, card.CreatedAt);
}

/// <summary>Reads the caller's identity (their buyer id) from the JWT.</summary>
public static class CallerIdentity
{
    public static string BuyerId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name)
           ?? user.Identity?.Name
           ?? throw new UnauthorizedAccessException("The token carries no caller identity.");
}
