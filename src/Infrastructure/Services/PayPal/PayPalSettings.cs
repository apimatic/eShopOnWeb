using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section. The secret
/// values (client id / secret) are supplied out-of-band via .NET user-secrets or environment
/// configuration and are never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Always "sandbox" for this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Optional explicit API base address; when set it is used verbatim.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> override when provided,
    /// otherwise derived from <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com",
        };
    }

    /// <summary>Fails fast at startup if the credentials needed to call PayPal are missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set 'PayPal:ClientId' and 'PayPal:ClientSecret' " +
                "(e.g. via user-secrets) before starting the API.");
        }
    }
}
