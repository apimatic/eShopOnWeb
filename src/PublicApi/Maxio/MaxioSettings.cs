using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via user-secrets or environment variables, never via committed files.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseAddress()
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
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Provide it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Provide it via user-secrets or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        // GetBaseAddress throws when neither BaseUrl nor Subdomain is set.
        _ = GetBaseAddress();
    }
}
