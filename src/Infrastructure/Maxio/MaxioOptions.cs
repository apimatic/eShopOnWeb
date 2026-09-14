using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio"
/// configuration section using exactly these keys:
///   Maxio:ApiKey              (from MAXIO_API_KEY)
///   Maxio:Subdomain           (from MAXIO_SITE_SUBDOMAIN)
///   Maxio:ProductFamilyHandle (from MAXIO_DEFAULT_PRODUCT_FAMILY)
///   Maxio:BaseUrl             (optional verbatim API base address override)
/// Values are never hard-coded and never committed; they come from configuration.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim;
    /// otherwise the base address is derived from the site subdomain per the Maxio
    /// OpenAPI server template (https://{site}.chargify.com).
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new OptionsValidationException(CONFIG_SECTION_NAME, typeof(MaxioOptions),
                new[] { $"'{CONFIG_SECTION_NAME}:Subdomain' must be provided when '{CONFIG_SECTION_NAME}:BaseUrl' is not set." });
        }

        return $"https://{Subdomain.Trim().TrimEnd('/')}.chargify.com";
    }
}
