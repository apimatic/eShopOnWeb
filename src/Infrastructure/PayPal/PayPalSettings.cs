namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the "PayPal" configuration section (keys: ClientId, ClientSecret,
/// Environment, Currency, BaseUrl). Values arrive via environment variables /
/// user-secrets; none are stored in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"; selects the PayPal API server from the spec.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency used for every amount.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the base address for
    /// every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}
