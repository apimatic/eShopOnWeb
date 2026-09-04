namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A card presented for a one-off payment. Full details never persist.</summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public CardBillingAddress? BillingAddress { get; set; }
}

public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; } // state / province
    public string? AdminArea2 { get; set; } // city
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}