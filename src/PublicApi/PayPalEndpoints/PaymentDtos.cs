using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>A requested order line in the place-order request.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address in the place-order request.</summary>
public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

/// <summary>Raw card details in a pay or save-card request. Never stored or logged by this app.</summary>
public class CardDto
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }        // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }    // state / province
    public string? AdminArea2 { get; set; }    // city
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }   // ISO-3166-1 alpha-2
}

/// <summary>The caller's view of one order and its payment state.</summary>
public class OrderPaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
}

public class RefundView
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalRefundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Safe description of a saved card — never full card details.</summary>
public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Shared mapping and request helpers for the PayPal endpoints.</summary>
internal static class PaymentMapping
{
    /// <summary>Resolves the authenticated shopper's id (their username/email) from the JWT.</summary>
    public static string? CurrentBuyerId(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name;

    public static CardDetails ToCardDetails(CardDto card) => new(
        card.Number ?? string.Empty,
        card.Expiry ?? string.Empty,
        card.SecurityCode ?? string.Empty,
        card.CardholderName,
        card.AddressLine1,
        card.AddressLine2,
        card.AdminArea1,
        card.AdminArea2,
        card.PostalCode,
        card.CountryCode);

    public static OrderPaymentView ToView(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PaymentMethod = payment.PaymentMethodDescription,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        CaptureId = payment.CaptureId,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.TotalRefunded(),
        CreatedAt = payment.CreatedAt,
        UpdatedAt = payment.UpdatedAt,
        Refunds = payment.Refunds
            .Select(r => new RefundView
            {
                RefundId = r.Id,
                Amount = r.Amount,
                Status = r.Status,
                PayPalRefundId = r.PayPalRefundId,
                CreatedAt = r.CreatedAt
            })
            .ToList()
    };

    public static SavedCardView ToView(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };
}
