namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details in transit to the payment gateway only.
/// Never persisted, never logged.
/// </summary>
public class GatewayCardDetails
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM format (PayPal's expected shape).</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}
