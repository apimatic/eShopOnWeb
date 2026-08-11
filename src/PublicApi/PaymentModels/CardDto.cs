using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>Raw card details posted by a caller for a one-off payment or to save a card.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;       // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardPaymentDetails ToDomain() => new(
        Number,
        Expiry,
        SecurityCode,
        Name,
        BillingAddress?.ToDomain());
}

public class BillingAddressDto
{
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }   // state / province
    public string? AdminArea2 { get; set; }   // city
    public string? PostalCode { get; set; }

    public CardBillingAddress ToDomain() =>
        new(CountryCode, AddressLine1, AddressLine2, AdminArea1, AdminArea2, PostalCode);
}
