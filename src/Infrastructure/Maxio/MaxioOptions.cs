using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Secret values (API key, subdomain)
/// are provided via user-secrets / environment variables and must never be committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio Advanced Billing API key (used as the Basic-auth username).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The site subdomain, used to derive the default API base URL
    /// (https://{subdomain}.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address
    /// instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
