using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ── Request DTOs (bound from JSON bodies) ─────────────────────────────────────
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public string? BillingLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingPostalCode { get; set; }

    public CardDetails ToCardDetails() => new(
        Number, Expiry, SecurityCode, CardholderName,
        BillingLine1, BillingCity, BillingState, BillingCountryCode, BillingPostalCode);
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundRequestDto
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : CardDto { }

// ── Response DTOs ─────────────────────────────────────────────────────────────
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

internal static class PaymentEndpointHelpers
{
    /// <summary>The shopper identity comes from the token's name claim; used as the order/card buyer id.</summary>
    public static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.PaymentNotFoundException("No authenticated user.");
}
