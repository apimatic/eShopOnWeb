using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;

/// <summary>Billing address for a card. Only <see cref="CountryCode"/> is required by PayPal.</summary>
public class BillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter country code, e.g. US.</summary>
    public string CountryCode { get; set; } = "US";

    public BillingAddressDetails ToBillingAddressDetails() => new BillingAddressDetails
    {
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        State = State,
        PostalCode = PostalCode,
        CountryCode = string.IsNullOrWhiteSpace(CountryCode) ? "US" : CountryCode
    };
}
