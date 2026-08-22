using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

public class CardDetailsDto
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

public class CardBillingAddressDto
{
    public string? CountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

public static class CardDetailsMapping
{
    public static CardPaymentSource ToSource(CardDetailsDto card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = card.BillingAddress is null
            ? null
            : new CardBillingAddress
            {
                CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                    ? "US"
                    : card.BillingAddress.CountryCode,
                AddressLine1 = card.BillingAddress.AddressLine1,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode
            }
    };
}
