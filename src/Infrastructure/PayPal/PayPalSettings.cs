namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
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
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}
