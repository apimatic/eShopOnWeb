using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed binding of the <c>PayPal:</c> configuration section. Values are supplied through
/// configuration (user-secrets in development) and never hard-coded, so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for every amount.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional API base-address override. When set, it is used verbatim for every PayPal call,
    /// including the token request, instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address for all calls.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }

    /// <summary>The currency to use, defaulting to USD if unset.</summary>
    public string ResolveCurrency() =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

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
    }
}
