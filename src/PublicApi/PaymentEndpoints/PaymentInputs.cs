using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied for a one-off payment or to be saved. Never stored or logged by this app.</summary>
public class CardInput
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressInput? BillingAddress { get; set; }

    public PayPalCard ToPayPalCard() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddress?.ToPayPalBillingAddress());
}

public class BillingAddressInput
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalBillingAddress ToPayPalBillingAddress() =>
        new(Line1, Line2, City, State, PostalCode, CountryCode);
}

public class ShipToAddressInputDto
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";

    public ShipToAddressInput ToInput() => new(Street, City, State, Country, ZipCode);
}
