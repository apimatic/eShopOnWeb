namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card data held only in memory while calling PayPal. Never persist or log this type.
/// </summary>
public sealed class CardDetails
{
    public CardDetails(
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

    public override string ToString() => "[redacted card details]";
}

public sealed class CardBillingAddress
{
    public CardBillingAddress(
        string addressLine1,
        string? addressLine2,
        string adminArea2,
        string adminArea1,
        string postalCode,
        string countryCode)
    {
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        AdminArea2 = adminArea2;
        AdminArea1 = adminArea1;
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public string AddressLine1 { get; }
    public string? AddressLine2 { get; }
    public string AdminArea2 { get; }
    public string AdminArea1 { get; }
    public string PostalCode { get; }
    public string CountryCode { get; }

    public override string ToString() => "[redacted billing address]";
}
