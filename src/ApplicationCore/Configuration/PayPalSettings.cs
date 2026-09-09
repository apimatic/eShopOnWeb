using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied via configuration/user-secrets and are never hard-coded, so the same build runs
/// against any PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>/<c>production</c>. Selects the default base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit base URL. When set it is used verbatim for every PayPal call (including
    /// the OAuth token request) instead of a URL derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// The API base address for every call. Prefers <see cref="BaseUrl"/> when set; otherwise
    /// derives it from <see cref="Environment"/>. Matches the server URLs in the OpenAPI specs.
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }
}
