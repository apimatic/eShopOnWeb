using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values must come from environment /
/// user-secrets, never from committed files.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from MAXIO_API_KEY.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from MAXIO_SITE_SUBDOMAIN. Used to derive https://{subdomain}.chargify.com when BaseUrl is unset.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from MAXIO_DEFAULT_PRODUCT_FAMILY. Catalog handle, not a numeric id.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of deriving one from Subdomain.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Advanced Billing API root.
    /// US hosting template from the official Maxio SDK: https://{site}.chargify.com
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return EnsureTrailingSlash(BaseUrl.Trim());
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return "https://invalid.local/";
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    private static string EnsureTrailingSlash(string url)
    {
        return url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
    }
}
