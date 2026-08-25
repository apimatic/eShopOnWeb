namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Raw card details submitted once for a one-off payment or to save a new card. Never persisted.</summary>
public class CardDetailsRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;

    /// <summary>Format YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
