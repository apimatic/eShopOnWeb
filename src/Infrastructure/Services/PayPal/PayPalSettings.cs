using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. None of these values are
/// hard-coded: they are supplied via user-secrets / environment so the same build can target a
/// different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live" — selects the default API host when <see cref="BaseUrl"/> is unset.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency for all amounts.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call —
    /// including the OAuth token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The base URL actually used for API calls.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var env = (Environment ?? "sandbox").Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }

    /// <summary>Fail fast if a required credential is missing, rather than calling PayPal blind.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        }
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured.");
        }
    }
}
