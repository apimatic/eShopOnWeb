using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" section. Values are never
/// hard-coded — they come from configuration/user-secrets (see PAYPAL_* environment variables).
/// </summary>
public class PayPalSettings : IPaymentSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "production"/"live". Used to derive the API base URL when
    /// <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Currency for all payments (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, it is used verbatim as the API base address for
    /// every PayPal call — including the token request — instead of deriving one from
    /// <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string ProductionBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address, honoring the override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl!.TrimEnd('/');

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "production" or "live" => ProductionBaseUrl,
            _ => SandboxBaseUrl
        };
    }
}
