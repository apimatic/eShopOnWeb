namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card details supplied by a shopper for a one-off payment or to save a card. These are forwarded to PayPal
/// and never persisted or logged by this application.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number (spaces allowed; stripped before use).</summary>
    public string? Number { get; set; }

    /// <summary>Expiry as YYYY-MM (also accepts MM/YY or MM/YYYY).</summary>
    public string? Expiry { get; set; }

    /// <summary>Card security code (CVV).</summary>
    public string? SecurityCode { get; set; }

    public string? CardholderName { get; set; }

    // Billing address. Country code defaults to "US" when omitted (PayPal requires a country code).
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
}
