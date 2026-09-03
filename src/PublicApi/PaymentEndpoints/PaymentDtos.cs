using System.Collections.Generic;
using System.Security.Claims;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied by the caller. Never stored or logged by this application.</summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }   // state / province
    public string? AdminArea2 { get; set; }   // city
    public string? PostalCode { get; set; }
}

public class ShippingAddressDto
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

/// <summary>Shared mapping and identity helpers for the payment endpoints.</summary>
public static class PaymentEndpointExtensions
{
    public static CardInput? ToCardInput(this CardDetailsDto? dto)
    {
        if (dto is null) return null;
        return new CardInput(
            Number: dto.Number,
            Expiry: dto.Expiry,
            SecurityCode: dto.SecurityCode,
            CardholderName: dto.CardholderName,
            BillingAddress: dto.BillingAddress is null ? null : new BillingAddressInput(
                CountryCode: dto.BillingAddress.CountryCode,
                AddressLine1: dto.BillingAddress.AddressLine1,
                AddressLine2: dto.BillingAddress.AddressLine2,
                AdminArea1: dto.BillingAddress.AdminArea1,
                AdminArea2: dto.BillingAddress.AdminArea2,
                PostalCode: dto.BillingAddress.PostalCode));
    }

    public static ShippingAddressInput? ToShippingInput(this ShippingAddressDto? dto) =>
        dto is null ? null : new ShippingAddressInput(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);

    public static IReadOnlyList<OrderLineInput> ToLineInputs(this IEnumerable<OrderLineDto>? items)
    {
        var list = new List<OrderLineInput>();
        if (items is not null)
            foreach (var i in items)
                list.Add(new OrderLineInput(i.CatalogItemId, i.Quantity));
        return list;
    }

    /// <summary>The authenticated caller's id from the JWT (ClaimTypes.Name).</summary>
    public static string BuyerId(this ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name;
        Guard.Against.NullOrEmpty(name, nameof(name));
        return name;
    }
}
