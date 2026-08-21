using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values must come from
/// environment variables or user-secrets — never from source-controlled files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>Maxio:ApiKey</c> / <c>MAXIO_API_KEY</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:Subdomain</c> / <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:ProductFamilyHandle</c> / <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. Maps from <c>Maxio:BaseUrl</c>. When set, used verbatim as the
    /// Advanced Billing API base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim().TrimEnd('/') + "/";
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                throw new MaxioConfigurationException("Maxio:BaseUrl is not a valid absolute URI.");
            }

            return uri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Set Maxio:Subdomain or Maxio:BaseUrl.");
        }

        // Spec servers: https://{site}.chargify.com
        return new Uri($"https://{Subdomain.Trim()}.chargify.com/");
    }
}
