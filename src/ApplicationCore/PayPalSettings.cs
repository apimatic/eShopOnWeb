namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment-specific configuration - never hard-coded or committed.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// "sandbox" or "live". Determines the PayPal API base address unless
    /// <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// ISO-4217 currency code used for all charges (e.g. "USD").
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
