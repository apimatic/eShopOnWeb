using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Raw card details for a one-off payment or for saving a card. Processed in
/// transit only: never persisted, never logged.
/// </summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        return new CardDetails(Number, Expiry, SecurityCode, Name,
            BillingAddress is null ? null : new BillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.City,
                BillingAddress.State,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode));
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
