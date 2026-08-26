using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets/environment variables, never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (from MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN), e.g. "my-site" for my-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl
            : $"https://{Subdomain}.chargify.com";

        // Relative request URIs combine correctly only when the base address ends in a slash.
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Maxio base address '{baseUrl}' is not a valid absolute URI.");
        }

        return uri;
    }

    public void ThrowIfNotConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured. Set Maxio:ApiKey and Maxio:Subdomain " +
                "(or Maxio:BaseUrl) via user-secrets or environment variables.");
        }
    }
}
