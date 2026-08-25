using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Strongly-typed binding for the "Maxio" configuration section.
/// Values are supplied via environment/user-secrets; never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key (Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "my-site" for my-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        // Throws when neither BaseUrl nor Subdomain is set.
        _ = GetApiBaseUrl();
    }
}
