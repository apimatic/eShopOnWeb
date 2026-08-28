using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One line of a new order: a catalog item and how many of it. The price comes from the catalog.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    /// <summary>The identifier of the order just placed. Every later payment call is keyed on it.</summary>
    public int OrderId { get; set; }

    public OrderView? Order { get; set; }
}

/// <summary>Card details for a one-off payment. Never persisted and never logged.</summary>
public class CardDto
{
    public string? Number { get; set; }

    /// <summary>Expiry in <c>YYYY-MM</c> form.</summary>
    public string? Expiry { get; set; }

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    /// <summary>Keeps the card number out of anything that stringifies this object.</summary>
    public override string ToString() => "CardDto { redacted }";
}

public class BillingAddressDto
{
    /// <summary>Two-letter ISO country code — required by the processor on a billing address.</summary>
    public string? CountryCode { get; set; }

    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}

/// <summary>
/// Pay for an order with either a one-off card or one of the caller's own saved cards — exactly one
/// of the two.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route, never from the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    public CardDto? Card { get; set; }

    /// <summary>The id returned by <c>POST /api/payment-methods</c>.</summary>
    public int? PaymentMethodId { get; set; }

    public override string ToString() => "PayOrderRequest { redacted }";
}

public class PaymentResponse : BaseResponse
{
    public PaymentResponse(Guid correlationId) : base(correlationId) { }

    public PaymentResponse() { }

    public PaymentView? Payment { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Set from the route, never from the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Leave unset to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// The caller's own key. Repeating a request under the same key returns the first refund
    /// instead of refunding twice; two distinct keys are two legitimate partial refunds.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public RefundOrderResponse() { }

    /// <summary>The identifier of the refund just recorded.</summary>
    public string RefundId { get; set; } = string.Empty;

    public RefundView? Refund { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public MyOrdersResponse() { }

    public List<OrderView> Orders { get; set; } = new();
}

/// <summary>Reads the caller's identity from the token — never from the request body.</summary>
internal static class CallerIdentity
{
    public static string? BuyerId(this ClaimsPrincipal? user) =>
        string.IsNullOrWhiteSpace(user?.Identity?.Name) ? null : user!.Identity!.Name;

    public static string? BuyerId(this Microsoft.AspNetCore.Http.HttpContext context) =>
        context.User.BuyerId();
}

internal static class CardMapping
{
    public static CardDetails ToCardDetails(this CardDto card) => new()
    {
        Number = card.Number ?? string.Empty,
        Expiry = card.Expiry ?? string.Empty,
        SecurityCode = card.SecurityCode,
        CardholderName = card.CardholderName,
        BillingAddress = card.BillingAddress is null
            ? null
            : new CardBillingAddress
            {
                CountryCode = card.BillingAddress.CountryCode ?? string.Empty,
                Line1 = card.BillingAddress.Line1,
                Line2 = card.BillingAddress.Line2,
                City = card.BillingAddress.City,
                State = card.BillingAddress.State,
                PostalCode = card.BillingAddress.PostalCode
            }
    };
}
