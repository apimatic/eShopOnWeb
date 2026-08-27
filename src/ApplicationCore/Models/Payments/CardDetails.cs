namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, held in memory only for the duration of a single provider call.
/// Never persisted and never logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? Name { get; set; }
    public string? SecurityCode { get; set; }
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; } // state
    public string? AdminArea2 { get; set; } // city
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
