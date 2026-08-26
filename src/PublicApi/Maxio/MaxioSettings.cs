using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables / user-secrets, never committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Billing API key (used as the Basic-auth username; password is "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "mysite" for mysite.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: set either '{CONFIG_NAME}:BaseUrl' or '{CONFIG_NAME}:Subdomain'.");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"Maxio is not configured: '{CONFIG_NAME}:ApiKey' is required.");
        }

        _ = GetBaseUrl();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException($"Maxio is not configured: '{CONFIG_NAME}:ProductFamilyHandle' is required.");
        }
    }
}
