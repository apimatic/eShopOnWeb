namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied through
/// environment variables / user-secrets — never hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}
