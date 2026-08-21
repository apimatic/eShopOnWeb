namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardPaymentDetails
{
    public CardPaymentDetails(
        string number,
        string expiry,
        string securityCode,
        string name,
        CardBillingAddress billingAddress)
    {
        Number = number;
        Expiry = expiry;
        SecurityCode = securityCode;
        Name = name;
        BillingAddress = billingAddress;
    }

    public string Number { get; }
    public string Expiry { get; }
    public string SecurityCode { get; }
    public string Name { get; }
    public CardBillingAddress BillingAddress { get; }
}

public sealed class CardBillingAddress
{
    public CardBillingAddress(
        string addressLine1,
        string? addressLine2,
        string adminArea1,
        string adminArea2,
        string postalCode,
        string countryCode)
    {
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        AdminArea1 = adminArea1;
        AdminArea2 = adminArea2;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public string AddressLine1 { get; }
    public string? AddressLine2 { get; }
    public string AdminArea1 { get; }
    public string AdminArea2 { get; }
    public string PostalCode { get; }
    public string CountryCode { get; }
}
