using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Raw card details. Used only in transit to PayPal: never persisted, never logged.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in Internet date format, e.g. "2028-04".</summary>
    public string Expiry { get; set; } = string.Empty;

    [JsonPropertyName("securityCode")]
    public string? SecurityCode { get; set; }

    public string? Name { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public PayPalCardDetails ToPayPalCard() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToPayPalAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public PayPalBillingAddress ToPayPalAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
