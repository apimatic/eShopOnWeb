namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the "Maxio" configuration
/// section. Values are supplied via environment variables / user-secrets and are
/// never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Site API key (from MAXIO_API_KEY). Used as the HTTP Basic username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose plans are subscribable (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the
    /// base URL is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the <see cref="BaseUrl"/> override when
    /// present, otherwise the standard <c>https://{subdomain}.chargify.com</c> host.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
