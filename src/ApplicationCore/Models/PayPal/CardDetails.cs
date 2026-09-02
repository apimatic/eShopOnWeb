namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>
/// Full card details used only in transit to PayPal. Never persisted, never logged.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    /// <summary>PayPal expiry format: YYYY-MM.</summary>
    public string Expiry => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
}
