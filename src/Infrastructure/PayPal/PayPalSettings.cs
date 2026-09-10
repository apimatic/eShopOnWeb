using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied at runtime
/// (from user-secrets / environment) and never hard-coded, so the same build runs against any account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>/<c>production</c>. Determines the default API base address.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The three-letter ISO-4217 currency for all payments, e.g. USD.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call — including the
    /// token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address: the explicit override if present, else derived from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }
}
