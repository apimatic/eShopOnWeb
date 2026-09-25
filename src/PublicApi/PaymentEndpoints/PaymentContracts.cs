using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details for a one-off payment or to vault. Full details are never stored or logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddressLine1, BillingState, BillingCity, BillingPostalCode, BillingCountryCode);
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

// ----- Requests -----

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipTo { get; set; }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }   // bound from the route, server-side
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class OrderActionRequest
{
    public int OrderId { get; set; }   // bound from the route
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }   // bound from the route
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

// ----- Responses (identifiers surface as top-level fields) -----

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public OrderView? Order { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

internal static class CallerIdentity
{
    /// <summary>The signed-in shopper's id (the JWT name claim). Empty when unauthenticated.</summary>
    public static string GetBuyerId(this HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name ?? string.Empty;
}
