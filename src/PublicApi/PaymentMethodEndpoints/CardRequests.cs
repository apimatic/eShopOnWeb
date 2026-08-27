using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Card details supplied in flight for a one-off payment or for saving a card.
/// Never persisted and never logged.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToDetails());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public BillingAddressDetails ToDetails() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
