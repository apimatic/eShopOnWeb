namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment variables — never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Determines the PayPal API base address unless BaseUrl is set.</summary>
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, used verbatim as the base address for every
    /// PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
