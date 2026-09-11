using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied via environment variables
/// loaded into user-secrets — never hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBase = "https://api-m.sandbox.paypal.com";
    private const string LiveBase = "https://api-m.paypal.com";

    /// <summary>Resolve the API base address per the credentials rules.</summary>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return new Uri(BaseUrl, UriKind.Absolute);

        var isLive = Environment?.Trim().ToLowerInvariant() is "live" or "production";
        return new Uri(isLive ? LiveBase : SandboxBase, UriKind.Absolute);
    }
}
