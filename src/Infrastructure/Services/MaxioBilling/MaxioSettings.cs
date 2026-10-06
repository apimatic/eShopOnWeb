using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Services.MaxioBilling;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; values are expected to be
/// provided via user-secrets or environment variables — never hard-coded.
///
/// Expected keys (user-secrets / environment):
///   Maxio:ApiKey                (MAXIO_API_KEY)
///   Maxio:Subdomain             (MAXIO_SITE_SUBDOMAIN)
///   Maxio:ProductFamilyHandle   (MAXIO_DEFAULT_PRODUCT_FAMILY)
///   Maxio:BaseUrl               (optional override; when set it is used
///                                verbatim as the API base address instead
///                                of being derived from the subdomain)
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The base address of the Maxio Advanced Billing API. When Maxio:BaseUrl
    /// is configured it is used verbatim; otherwise it is derived from the
    /// site subdomain.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com";
    }

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"Maxio:{nameof(ApiKey)} (MAXIO_API_KEY)");
        if (string.IsNullOrWhiteSpace(Subdomain)) missing.Add($"Maxio:{nameof(Subdomain)} (MAXIO_SITE_SUBDOMAIN)");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"Maxio:{nameof(ProductFamilyHandle)} (MAXIO_DEFAULT_PRODUCT_FAMILY)");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing settings are missing required configuration keys. " +
                $"Provide them via user-secrets or environment variables: {string.Join(", ", missing)}.");
        }
    }
}