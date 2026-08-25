namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from configuration section "PayPal". Values come from environment/user-secrets
/// (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY), never
/// hard-coded, so the same build can run against a different PayPal account.
/// </summary>
public class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }
}
