namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied through
/// configuration/user-secrets (from the PAYPAL_* environment variables) and are never hard-coded.
/// </summary>
public class PayPalConfiguration
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id (from PAYPAL_CLIENT_ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live" (from PAYPAL_ENVIRONMENT).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts (from PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call
    /// (including the token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base address: the explicit override if set, otherwise derived from the environment.</summary>
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
