namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via
/// environment variables / user-secrets (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET,
/// PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY) and must never be committed to the repo.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live". Determines the PayPal API base address unless BaseUrl is set.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for all charges (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>Optional override. When set, used verbatim as the base address for every
    /// PayPal call (including the OAuth token request) instead of deriving one from Environment.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        // Server URL from the PayPal OpenAPI specifications (api-specs/paypal).
        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
