using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// No default values are baked in: the same build must be able to run against any Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Site API key. Source env var: MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain (e.g. "cp-exp-7"). Source env var: MAXIO_SITE_SUBDOMAIN.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds this storefront's plans. Source env var: MAXIO_DEFAULT_PRODUCT_FAMILY.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Advanced Billing API base address. When set it is used verbatim
    /// (as the API base address) instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Honors <see cref="BaseUrl"/> when provided; otherwise derives
    /// the US (chargify.com) endpoint from the site subdomain.
    /// </summary>
    public string GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:ApiKey (MAXIO_API_KEY), Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN), " +
                "Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY), or provide Maxio:BaseUrl.");
        }

        return $"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com/";
    }
}
