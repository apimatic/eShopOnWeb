using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by the caller for a one-off payment or to save. These are forwarded
/// straight to PayPal and never persisted in this application's database or written to logs.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM (Internet date format), e.g. 2030-04.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public PayPalCard ToPayPalCard() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress?.ToPayPalBillingAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO 3166-1 country code, e.g. US.</summary>
    public string CountryCode { get; set; } = "US";

    public PayPalBillingAddress ToPayPalBillingAddress() => new(
        AddressLine1,
        AddressLine2,
        City,
        State,
        PostalCode,
        CountryCode);
}
