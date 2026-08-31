namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Card details for a one-off payment or for saving a card. Full card data is passed through to
/// PayPal only — never persisted, never logged.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
