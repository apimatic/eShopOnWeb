using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal transport settings, bound from the <c>PayPal:</c> configuration section. Values
/// are supplied through configuration / user-secrets and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>/<c>production</c>.</summary>
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call (including the token request) instead of deriving one from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective base address to use for all PayPal REST calls.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl,
        };
    }
}
