using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied for a one-off payment or to vault. Full card data is never stored or logged.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM", e.g. "2030-01".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToGateway());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }

    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }

    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }

    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public GatewayBillingAddress ToGateway() =>
        new(AddressLine1, AdminArea1, AdminArea2, PostalCode, CountryCode);
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Resolves the caller's identity from the JWT — the buyer id is always the token, never the body.</summary>
public static class BuyerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var id = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(id))
        {
            throw new PaymentException("The caller identity could not be determined from the token.", 401);
        }
        return id;
    }
}
