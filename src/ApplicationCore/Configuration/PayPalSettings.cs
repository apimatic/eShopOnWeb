using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied through configuration (user-secrets / environment) and are never hard-coded, so the
/// same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Selects the default API base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all amounts (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth token request — instead of one derived from
    /// <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address per the contract: explicit <see cref="BaseUrl"/> wins;
    /// otherwise it is derived from <see cref="Environment"/> (sandbox uses the sandbox host).</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = (Environment ?? string.Empty).Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}
