using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ------------------------------------------------------------------ Request models

public class OrderLineApiModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressApiModel
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class BillingAddressApiModel
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class CardApiModel
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in "YYYY-MM" form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressApiModel? BillingAddress { get; set; }
}

public class PlaceOrderApiRequest
{
    public List<OrderLineApiModel> Items { get; set; } = new();
    public AddressApiModel? ShipToAddress { get; set; }
}

public class PayOrderApiRequest
{
    public CardApiModel? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundApiRequest
{
    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>Amount to refund; omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }
}

public class SavePaymentMethodApiRequest
{
    public CardApiModel Card { get; set; } = new();
}

// ------------------------------------------------------------------ Helpers

internal static class PaymentApiMapping
{
    public static CardDetails ToCardDetails(this CardApiModel model)
    {
        var billing = model.BillingAddress is null
            ? null
            : new CardBillingAddress(
                model.BillingAddress.AddressLine1,
                model.BillingAddress.City,
                model.BillingAddress.State,
                model.BillingAddress.PostalCode,
                model.BillingAddress.CountryCode);

        return new CardDetails(
            (model.Number ?? string.Empty).Replace(" ", string.Empty),
            model.Expiry,
            model.SecurityCode ?? string.Empty,
            string.IsNullOrWhiteSpace(model.Name) ? "Card Holder" : model.Name!,
            billing);
    }

    public static Address ToAddress(this AddressApiModel? model)
    {
        // ShipToAddress is required on an Order; default sensibly when the caller omits it.
        return new Address(
            street: Coalesce(model?.Street, "N/A"),
            city: Coalesce(model?.City, "N/A"),
            state: Coalesce(model?.State, "N/A"),
            country: Coalesce(model?.Country, "N/A"),
            zipcode: Coalesce(model?.ZipCode, "00000"));
    }

    public static IReadOnlyCollection<OrderLineRequest> ToOrderLines(this IEnumerable<OrderLineApiModel> items) =>
        items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;
}

internal static class CallerIdentity
{
    /// <summary>The signed-in shopper's id (the username stored in the JWT's Name claim).</summary>
    public static string BuyerId(this ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name ?? user?.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(name))
        {
            throw new UnauthorizedAccessException("The caller is not authenticated.");
        }
        return name;
    }
}
