using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Secret values are supplied via
/// environment variables / user-secrets, never committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maps from <c>Maxio:ApiKey</c> (environment: MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:Subdomain</c> (environment: MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maps from <c>Maxio:ProductFamilyHandle</c> (environment: MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. Maps from <c>Maxio:BaseUrl</c>. When set, used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the Billing API root. Documented form is https://{subdomain}.chargify.com
    /// unless <see cref="BaseUrl"/> overrides it.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Configure Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
