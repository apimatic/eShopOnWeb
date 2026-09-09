namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or for saving a card. These are passed straight through to
/// PayPal and are never persisted in this application's database, nor written to logs.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in <c>YYYY-MM</c> format, as PayPal expects.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }

    public string? Name { get; set; }

    public CardBillingAddress? BillingAddress { get; set; }
}

/// <summary>The card's billing address. <see cref="CountryCode"/> is the only field PayPal requires.</summary>
public class CardBillingAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
