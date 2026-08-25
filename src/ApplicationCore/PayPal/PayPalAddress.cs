namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalAddress
{
    public PayPalAddress(string countryCode, string addressLine1, string city, string? state, string postalCode, string? addressLine2 = null)
    {
        CountryCode = countryCode;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        State = state;
        PostalCode = postalCode;
    }

    public string CountryCode { get; }
    public string AddressLine1 { get; }
    public string? AddressLine2 { get; }
    public string City { get; }
    public string? State { get; }
    public string PostalCode { get; }
}
