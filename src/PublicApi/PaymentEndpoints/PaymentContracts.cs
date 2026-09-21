using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- request DTOs ----

public record CardDto(
    string Number,
    string Expiry,               // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    string? BillingLine1,
    string? BillingCity,
    string? BillingState,
    string? BillingCountryCode,
    string? BillingPostalCode);

public record OrderLineDto(int CatalogItemId, int Quantity);

public record ShipToDto(string Street, string City, string State, string Country, string ZipCode);

public record PlaceOrderRequest(List<OrderLineDto>? Items, ShipToDto? ShipToAddress);

public record PayOrderRequest(CardDto? Card, int? SavedCardId);

public record RefundOrderRequest(decimal? Amount, string? IdempotencyKey);

public record SavePaymentMethodRequest(CardDto? Card);

// ---- response DTOs (only the *Id fields are contractually required to be top-level) ----

public record CreateOrderResponse(int OrderId);

public record CreatePaymentMethodResponse(int PaymentMethodId, string? Brand, string? Last4, string? Expiry);

public record CreateRefundResponse(string RefundId, RefundView Refund);

/// <summary>Shared mapping and identity helpers for the payment endpoints.</summary>
public static class PaymentEndpointHelpers
{
    /// <summary>The signed-in shopper's identity, taken from the JWT name claim.</summary>
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("unique_name")
                   ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return name ?? string.Empty;
    }

    public static CardDetails? ToDomain(this CardDto? card) =>
        card is null
            ? null
            : new CardDetails(
                card.Number,
                card.Expiry,
                card.SecurityCode,
                card.CardholderName,
                card.BillingLine1,
                card.BillingCity,
                card.BillingState,
                card.BillingCountryCode,
                card.BillingPostalCode);
}
