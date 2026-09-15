namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values come from configuration / user-secrets
/// (sourced from the PAYPAL_* environment variables); none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Selects the default API base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for order amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every PayPal
    /// call (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The resolved base URL: the explicit override if present, otherwise per environment.</summary>
    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/');
            }

            var env = (Environment ?? string.Empty).Trim().ToLowerInvariant();
            var isLive = env is "live" or "production" or "prod";
            return isLive ? LiveBaseUrl : SandboxBaseUrl;
        }
    }
}
