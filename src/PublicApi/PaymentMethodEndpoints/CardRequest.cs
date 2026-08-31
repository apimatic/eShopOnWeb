namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details, accepted only over TLS and forwarded to PayPal. They are never
/// persisted in the application's database and never written to logs.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Card expiration year and month, e.g. "2028-04".
    /// </summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
