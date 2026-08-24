using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment/user-secrets (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY); no secret values are stored in this repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic auth username; password is literally "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Maxio Advanced Billing site (the {site} template variable in the spec's server URL).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscription plans offered in the shop.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving the base address from <see cref="Subdomain"/> using the spec's US production
    /// server template (https://{site}.chargify.com).
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";
        return new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Provide it via the MAXIO_API_KEY environment variable or user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Provide it via the MAXIO_SITE_SUBDOMAIN environment variable or user-secrets (or set Maxio:BaseUrl explicitly).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Provide it via the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or user-secrets.");
        }
    }
}
