using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardPaymentSource ToSource()
    {
        if (string.IsNullOrWhiteSpace(Number) || string.IsNullOrWhiteSpace(Expiry) || string.IsNullOrWhiteSpace(SecurityCode))
        {
            throw new ApplicationCore.Exceptions.CheckoutException("Card number, expiry (YYYY-MM), and security code are required.", 400);
        }

        CardBillingAddress? address = null;
        if (BillingAddress is not null)
        {
            if (string.IsNullOrWhiteSpace(BillingAddress.CountryCode))
            {
                throw new ApplicationCore.Exceptions.CheckoutException("Billing address countryCode is required when an address is supplied.", 400);
            }

            address = new CardBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.AdminArea2,
                BillingAddress.AdminArea1,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode);
        }

        return new CardPaymentSource(Number.Replace(" ", string.Empty), Expiry, SecurityCode, Name, address);
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public class ShippingAddressDto
{
    public string Street { get; set; } = "123 Main St.";
    public string City { get; set; } = "Kent";
    public string State { get; set; } = "OH";
    public string Country { get; set; } = "United States";
    public string ZipCode { get; set; } = "44240";
}
