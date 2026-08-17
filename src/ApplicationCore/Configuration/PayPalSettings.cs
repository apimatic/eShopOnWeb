using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal configuration bound from the <c>PayPal:</c> section.
/// Values are supplied via environment variables loaded into .NET user-secrets and are never
/// hard-coded in the repository. Keys: <c>PayPal:ClientId</c>, <c>PayPal:ClientSecret</c>,
/// <c>PayPal:Environment</c>, <c>PayPal:Currency</c>, <c>PayPal:BaseUrl</c>.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of being derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        !string.Equals(Environment?.Trim(), "live", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Environment?.Trim(), "production", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves the API base address: explicit override wins; otherwise derived from environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return IsSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";
    }

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
