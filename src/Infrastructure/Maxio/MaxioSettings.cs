using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the Basic-auth username (password is "x" per the Maxio OpenAPI spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, used to template the spec server URL https://{site}.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family containing the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving from <see cref="Subdomain"/>.</summary>
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
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set the MAXIO_API_KEY environment variable or the Maxio:ApiKey user-secret.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable or the Maxio:Subdomain user-secret.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or the Maxio:ProductFamilyHandle user-secret.");
    }
}
