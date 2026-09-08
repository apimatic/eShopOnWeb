using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio (Advanced Billing) integration. Bound from the "Maxio"
/// configuration section. Values are never hard-coded; they are supplied through
/// configuration (e.g. .NET user-secrets seeded from environment variables).
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscribable plans.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim instead of
    /// deriving a base address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri BuildApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.EndsWith("/", StringComparison.Ordinal) ? BaseUrl : BaseUrl + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:Subdomain' is missing. Set Maxio:Subdomain (and Maxio:ApiKey) or provide Maxio:BaseUrl.");
        }

        // US-environment default host (Advanced Billing sandboxes live here too). Sites hosted
        // in other environments, or behind an API gateway, must set Maxio:BaseUrl explicitly.
        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public string ResolveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Set Maxio:ApiKey from the MAXIO_API_KEY environment variable.");
        }

        return ApiKey;
    }

    public string ResolveProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing. Set Maxio:ProductFamilyHandle from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        return ProductFamilyHandle;
    }
}
