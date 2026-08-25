namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Bound from the "PayPal" configuration section. Values are supplied via .NET user-secrets
// (populated from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET / PAYPAL_ENVIRONMENT /
// PAYPAL_CURRENCY environment variables) - never hard-coded and never committed.
public class PayPalOptions
{
    public const string ConfigSection = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    // "sandbox" or "live". Ignored when BaseUrl is set.
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    // Optional override. When set, used verbatim as the API base address for every PayPal
    // call, including the OAuth2 token request, instead of deriving one from Environment.
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
