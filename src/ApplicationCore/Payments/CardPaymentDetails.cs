namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class CardPaymentDetails
{
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public string? Name { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = "US";
}
