namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// Raw card details supplied by the caller for a one-off payment or to save a card. This app never
/// persists or logs the number/CVC; it is passed straight to PayPal and discarded.
/// </summary>
public class CardDto
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Expiry month, 1-12.</summary>
    public int ExpiryMonth { get; set; }

    /// <summary>Expiry year, 4-digit (2-digit is also accepted and expanded to 20xx).</summary>
    public int ExpiryYear { get; set; }

    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    public string? BillingLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    /// <summary>Two-letter billing country code. Defaults to "US" if omitted.</summary>
    public string? BillingCountryCode { get; set; }
}
