using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Card details supplied for a one-off payment or for saving a card. These flow through to
/// PayPal and are never persisted or logged. Expiry is YYYY-MM.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToModel()
    {
        return new CardDetails(Number, Expiry, SecurityCode, Name, BillingAddress?.ToModel());
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

    public BillingAddress ToModel()
    {
        return new BillingAddress(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
    }
}
