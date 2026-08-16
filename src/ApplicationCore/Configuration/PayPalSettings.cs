using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> configuration section.
/// None of these values are hard-coded anywhere; they are supplied through configuration
/// (user-secrets in development) so the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Determines the default API base address.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code that every amount is expressed in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set (non-empty), it is used verbatim for every
    /// PayPal call — including the OAuth token request — instead of one derived from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// The effective API base address: the explicit <see cref="BaseUrl"/> override if present,
    /// otherwise derived from <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}
