namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. All values come from configuration
/// (user-secrets / environment) — nothing is hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency all payments are taken in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every PayPal
    /// call (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address, with no trailing slash.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl
        };
    }
}
