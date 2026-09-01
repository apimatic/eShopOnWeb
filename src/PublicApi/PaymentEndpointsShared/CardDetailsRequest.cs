using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

/// <summary>
/// Full card details for a one-off payment or for saving a card. Used only in transit
/// to PayPal; never persisted, never logged.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToModel()
        => new CardDetails(Number, Expiry, SecurityCode, CardholderName, BillingAddress?.ToModel());
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public BillingAddress ToModel()
        => new BillingAddress(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
