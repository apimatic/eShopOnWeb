namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed settings for the PayPal integration, bound from the "PayPal" configuration section.
/// Values are supplied via environment variables / user-secrets and are never committed to the repo.
/// </summary>
public class PayPalSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every charge (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for EVERY PayPal call
    /// (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive =>
        Environment?.Trim().ToLowerInvariant() is "live" or "production";

    /// <summary>The API base address to use for all calls, honoring the BaseUrl override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        return IsLive
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
