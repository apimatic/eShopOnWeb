using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Raw card details for a one-off payment or for saving a card.
/// Never persisted by this application and never logged.
/// </summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        AddressLine1 = BillingAddressLine1,
        AddressLine2 = BillingAddressLine2,
        City = BillingCity,
        State = BillingState,
        PostalCode = BillingPostalCode,
        CountryCode = BillingCountryCode
    };
}
