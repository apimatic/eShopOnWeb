using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are never
/// hard-coded — they come from configuration (user-secrets in development, environment/secret store in
/// production) so the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings : IPaymentSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Drives the default API base address.</summary>
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call —
    /// including the OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The API base address to use for all calls, honoring the <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl
        };
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
