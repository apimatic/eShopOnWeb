namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to save a card. These values are forwarded to
/// PayPal and are never persisted in this application's database nor written to logs.
/// </summary>
public class CardRequest
{
    /// <summary>Primary account number, e.g. 4111111111111111 for the sandbox Visa test card.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2030-01. Any future date works in sandbox.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVV/CVC).</summary>
    public string? SecurityCode { get; set; }

    /// <summary>Name as it appears on the card.</summary>
    public string CardholderName { get; set; } = string.Empty;

    public BillingAddressRequest? BillingAddress { get; set; }
}

/// <summary>Billing address for the card. All fields optional; PayPal's field names are used internally.</summary>
public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
