namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Raw card details supplied by a shopper. Used only in-flight to reach PayPal; never persisted to the
/// application's database and never written to logs.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM" (also accepts "MM/YY" / "MM/YYYY").</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
