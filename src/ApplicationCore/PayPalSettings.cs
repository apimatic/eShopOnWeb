namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>PayPal</c> configuration section. Values come from environment
/// variables / user-secrets — never from source-controlled files.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When <see cref="BaseUrl"/> is set it is used as-is for every PayPal call,
    /// including the OAuth token request. Otherwise the host is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var isLive = string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase);

        return isLive
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
