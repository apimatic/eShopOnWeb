using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details supplied for a one-off payment or for saving a card.
/// Used only for the duration of the provider call; never persisted or logged.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? Name { get; set; }
    public string? SecurityCode { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToModel()
    {
        return new CardDetails
        {
            Number = Number,
            Expiry = Expiry,
            Name = Name,
            SecurityCode = SecurityCode,
            BillingAddress = BillingAddress == null ? null : new CardBillingAddress
            {
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                AdminArea1 = BillingAddress.AdminArea1,
                AdminArea2 = BillingAddress.AdminArea2,
                PostalCode = BillingAddress.PostalCode,
                CountryCode = BillingAddress.CountryCode
            }
        };
    }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
