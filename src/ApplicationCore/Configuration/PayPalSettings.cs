namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via
/// environment variables (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT,
/// PAYPAL_CURRENCY) or user-secrets; never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>"sandbox" or "live".</summary>
    public string? Environment { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Optional override for the PayPal API base address (used verbatim for every call, including the token request).</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
