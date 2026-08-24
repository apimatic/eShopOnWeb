using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables / user-secrets; none are hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic auth username; password is literally "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Maxio site, e.g. "acme" for acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving the US production URL (https://{subdomain}.chargify.com) from the subdomain.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : $"https://{Subdomain}.chargify.com";
        return new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable or the 'Maxio:ApiKey' user-secret.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable or the 'Maxio:Subdomain' user-secret.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or the 'Maxio:ProductFamilyHandle' user-secret.");
    }
}
