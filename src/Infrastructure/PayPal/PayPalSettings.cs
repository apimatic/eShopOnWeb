namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST API settings, bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment variables — none are hard-coded, so the same build runs against any PayPal app.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment. "sandbox" for development; anything else is treated as live.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Optional explicit API base address. When set it is used verbatim instead of deriving one from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return string.Equals(Environment, "sandbox", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";
    }
}
