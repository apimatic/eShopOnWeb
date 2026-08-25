namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Raw card fields for a one-off payment or to save a card. Never persisted -- forwarded to the
/// payment gateway for a single call and discarded.
/// </summary>
public class CardDetailsRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // "YYYY-MM"
    public string SecurityCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}
