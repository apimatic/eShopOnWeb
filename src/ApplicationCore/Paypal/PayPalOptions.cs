using System;

namespace Microsoft.eShopWeb.ApplicationCore.Paypal;

/// <summary>
/// Strongly-typed view of the <c>PayPal:</c> configuration section. Values are never hard-coded —
/// they are bound from configuration/user-secrets so the same build can run against a different
/// PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default family) or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code that all amounts are charged in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call,
    /// including the OAuth token request, instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolves the API base address: the explicit override if given, else derived from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return IsLive ? LiveBaseUrl : SandboxBaseUrl;
    }

    public bool IsLive =>
        string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fails fast at startup if a required credential/setting is missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured.");
    }
}
