using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

internal static class CallerIdentity
{
    public static string GetBuyerId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CheckoutException(401, "The caller is not authenticated.");
        }

        return name;
    }
}

internal static class CardRequestMapper
{
    public static CardPaymentSource ToCardSource(this CardDetailsRequest card) =>
        new(
            card.Number ?? string.Empty,
            card.Expiry ?? string.Empty,
            card.SecurityCode ?? string.Empty,
            card.Name ?? string.Empty,
            card.BillingAddress is null
                ? null
                : new CardBillingAddress(
                    card.BillingAddress.CountryCode ?? "US",
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.AdminArea2 ?? card.BillingAddress.City,
                    card.BillingAddress.AdminArea1 ?? card.BillingAddress.State,
                    card.BillingAddress.PostalCode ?? card.BillingAddress.ZipCode));
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
}
